using AxaptaCOMConnector;
using IND_CRM_API.Helpers;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Configuration;
using System.IO;

namespace IND_CRM_API.Services
{
    // Servicio encargado de gestionar sesiones contra Axapta 3.0
    // Ahora usa sesion por request para evitar compartir instancias COM.
    public class AxaptaSessionManager : IDisposable, IAxaptaSessionManager
    {
        // Niveles de severidad para logging
        public enum LogLevel { Info, Warning, Error }

        // Logger inyectado (por ahora FileAxLogger, pero facilmente sustituible)
        private static IAxLogger _logger = new FileAxLogger();

        // Passwords por usuario (cache)
        private readonly ConcurrentDictionary<string, string> _passwordByUser =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Tokens -> usuario (para compatibilidad con CallMethodByToken)
        private readonly ConcurrentDictionary<string, string> _tokenToUser =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Configuracion de Axapta
        private readonly string _configPath = AppSettingsHelper.GetSetting("AxConfigFile", "INDCRM_AX_CONFIG_FILE");
        private readonly string _defaultUser = AppSettingsHelper.GetSetting("Axapta.User", "USER_DEFAULT");
        private readonly string _defaultPass = AppSettingsHelper.GetSetting("Axapta.Password", "USER_PASS_DEFAULT");
        private readonly bool _verbose = AppSettingsHelper.GetBoolSetting("Axapta.VerboseLogging", false, "INDCRM_AX_VERBOSE_LOGGING");
        private readonly string _verbosePath = AppSettingsHelper.GetSetting("Axapta.VerboseLogPath", "INDCRM_AX_VERBOSE_LOG_PATH") ?? @"C:\INDAxaptaLogs";

        // Flag para permitir o no el uso de credenciales por defecto
        private readonly bool _allowDefaultCredentials = true;

        private readonly IND_AxSessionGuard _sessionGuard;

        // Constructor
        public AxaptaSessionManager() : this(null) { }

        // Nuevo constructor inyectable
        public AxaptaSessionManager(IAxLogger logger)
        {
            if (logger != null)
                _logger = logger;

            if (_verbose && !Directory.Exists(_verbosePath))
                Directory.CreateDirectory(_verbosePath);

            var allowDefaultSetting = AppSettingsHelper.GetSetting("Axapta.AllowDefaultCredentials", "INDCRM_AX_ALLOW_DEFAULT_CREDENTIALS");
            if (!string.IsNullOrWhiteSpace(allowDefaultSetting))
            {
                if (bool.TryParse(allowDefaultSetting, out var parsed))
                    _allowDefaultCredentials = parsed;
            }

            _sessionGuard = new IND_AxSessionGuard(_logger, _configPath, _defaultUser, _defaultPass, _allowDefaultCredentials);
        }

        // Helper de logging interno
        private static void Log(string message, LogLevel level = LogLevel.Info)
        {
            _logger.Log(message, level);
        }

        public static void LogStatic(string message)
        {
            _logger.Log("[API] " + message, LogLevel.Info);
        }

        // ---------------------------------------------------------
        // REQUEST SCOPE
        // ---------------------------------------------------------
        // Starts request scope in AsyncLocal for this request.
        public void BeginRequestScope(string correlationId, string traceId, string endpoint, string company)
        {
            IND_AxRequestContext.Start(correlationId, traceId, endpoint, company);
        }

        // Ends request scope and disposes the Axapta instance if any.
        public void EndRequestScope()
        {
            var ctx = IND_AxRequestContext.Current;
            if (ctx == null)
                return;

            if (ctx.AxInstance != null)
                _sessionGuard.SafeLogoffAndDispose(ctx.AxInstance, ctx, "request-end");

            IND_AxRequestContext.Clear();
        }

        // ---------------------------------------------------------
        // CREACION / SESIONES
        // ---------------------------------------------------------
        public bool CreateOrGetSession(string username, string password, JwtService.JwtTokenInfo tokenInfo)
        {
            try
            {
                var resolvedUser = string.IsNullOrWhiteSpace(username) ? _defaultUser : username;
                if (string.IsNullOrWhiteSpace(resolvedUser))
                {
                    Log("[AUTH] Missing Axapta username.", LogLevel.Warning);
                    return false;
                }

                var resolvedPassword = ResolvePassword(resolvedUser, password, out var passwordSource);
                if (string.IsNullOrWhiteSpace(resolvedPassword))
                {
                    Log($"[AUTH] Missing Axapta password for {resolvedUser}.", LogLevel.Warning);
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(password) && _passwordByUser.TryGetValue(resolvedUser, out var stored) &&
                    !string.Equals(stored, password, StringComparison.Ordinal))
                {
                    Log($"[SESSION-PASSWORD-ROTATION] {resolvedUser} -> password mismatch, forcing relogon.", LogLevel.Warning);
                }

                var ctx = IND_AxRequestContext.Current;
                var smokeOk = _sessionGuard.SmokeTest(resolvedUser, resolvedPassword, ctx);
                if (!smokeOk)
                {
                    Log($"[AUTH-FAIL] Axapta smoke test failed for {resolvedUser}.", LogLevel.Warning);
                    return false;
                }

                _passwordByUser[resolvedUser] = resolvedPassword;

                if (tokenInfo != null)
                    _tokenToUser[tokenInfo.Token] = resolvedUser;

                Log($"[AUTH-OK] Axapta session validated for {resolvedUser} source={passwordSource}.", LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                Log($"[ERROR-SESSION] {username} -> {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        // Chooses the password source: provided, cached, or default.
        private string ResolvePassword(string username, string providedPassword, out string source)
        {
            source = "provided";
            if (!string.IsNullOrWhiteSpace(providedPassword))
                return providedPassword;

            if (_passwordByUser.TryGetValue(username, out var stored) && !string.IsNullOrWhiteSpace(stored))
            {
                source = "cache";
                return stored;
            }

            if (_allowDefaultCredentials)
            {
                source = "default";
                return _defaultPass;
            }

            source = "none";
            return null;
        }

        // ---------------------------------------------------------
        // TOKENS
        // ---------------------------------------------------------
        // Updates token mapping after login/refresh.
        public bool RefreshSessionToken(string username, JwtService.JwtTokenInfo tokenInfo, string oldToken)
        {
            if (string.IsNullOrWhiteSpace(username) || tokenInfo == null)
                return false;

            if (!string.IsNullOrEmpty(oldToken) &&
                _tokenToUser.TryGetValue(oldToken, out var mappedUser) &&
                !string.Equals(mappedUser, username, StringComparison.OrdinalIgnoreCase))
            {
                return false; // token no pertenece al usuario autenticado
            }

            if (!string.IsNullOrEmpty(oldToken))
                _tokenToUser.TryRemove(oldToken, out _);

            _tokenToUser[tokenInfo.Token] = username;
            Log($"[SESSION-REFRESH-TOKEN] {username}", LogLevel.Info);
            return true;
        }

        // ---------------------------------------------------------
        // AX INSTANCE (PER REQUEST)
        // ---------------------------------------------------------
        // Returns per-request Axapta instance (creates on demand).
        public Axapta2Class GetAxInstanceForUser(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new Exception("Usuario no valido.");

            var ctx = IND_AxRequestContext.Current;
            if (ctx == null)
            {
                Log("[AX-SESSION] Missing request scope; creating transient context.", LogLevel.Warning);
                var fallbackCorrelationId = Guid.NewGuid().ToString("N");
                IND_AxRequestContext.Start(fallbackCorrelationId, Guid.NewGuid().ToString("N"), "unknown", string.Empty);
                ctx = IND_AxRequestContext.Current;
            }

            ctx.Username = username;

            if (ctx.AxInstance != null)
                return ctx.AxInstance;

            var resolvedPassword = ResolvePassword(username, null, out var source);
            if (string.IsNullOrWhiteSpace(resolvedPassword))
                throw new Exception("No hay credenciales disponibles para Axapta.");

            ctx.AxInstance = _sessionGuard.EnsureLoggedOn(username, resolvedPassword, ctx);
            Log($"[AX-SESSION] Session created user={username} source={source}.", LogLevel.Info);
            return ctx.AxInstance;
        }

        // ---------------------------------------------------------
        // LLAMADAS A AXAPTA
        // ---------------------------------------------------------
        public string CallMethodByToken(string token, string className, string methodName, object args = null)
        {
            if (!_tokenToUser.TryGetValue(token, out var username))
                throw new Exception("Invalid token.");

            return CallMethodByUser(username, className, methodName, args)?.ToString() ?? string.Empty;
        }

        // Executes an Axapta call with a single retry on session errors.
        public object CallMethodByUser(string username, string className, string methodName, object args = null)
        {
            var ctx = IND_AxRequestContext.Current;
            return _sessionGuard.ExecuteWithRetryOnSessionErrors(
                () =>
                {
                    var ax = GetAxInstanceForUser(username);
                    return InvokeAxMethod(ax, className, methodName, args);
                },
                () =>
                {
                    ResetRequestSession("retry");
                    var ax = GetAxInstanceForUser(username);
                    return InvokeAxMethod(ax, className, methodName, args);
                },
                ctx,
                className + "." + methodName
            );
        }

        // Executes an Axapta container call with a single retry on session errors.
        public IAxaptaContainer CallContainerMethodByUser(string username, string className, string methodName, object[] args = null)
        {
            var ctx = IND_AxRequestContext.Current;
            return _sessionGuard.ExecuteWithRetryOnSessionErrors(
                () =>
                {
                    var ax = GetAxInstanceForUser(username);
                    var result = InvokeAxMethod(ax, className, methodName, args);
                    var container = result as IAxaptaContainer;
                    if (container == null)
                        throw new Exception("El metodo no devolvio un AxaptaContainer valido.");
                    return container;
                },
                () =>
                {
                    ResetRequestSession("retry");
                    var ax = GetAxInstanceForUser(username);
                    var result = InvokeAxMethod(ax, className, methodName, args);
                    var container = result as IAxaptaContainer;
                    if (container == null)
                        throw new Exception("El metodo no devolvio un AxaptaContainer valido.");
                    return container;
                },
                ctx,
                className + "." + methodName
            );
        }

        // Low-level COM invocation helper.
        private object InvokeAxMethod(Axapta2Class ax, string className, string methodName, object args)
        {
            if (ax == null)
                throw new Exception("Instancia Axapta no valida.");

            if (args == null)
                return ax.CallStaticClassMethod(className, methodName);

            if (args is object[] arr)
                return ax.CallStaticClassMethod(className, methodName, arr);

            return ax.CallStaticClassMethod(className, methodName, new object[] { args });
        }

        // Disposes the current request session so it can be recreated on retry.
        private void ResetRequestSession(string reason)
        {
            var ctx = IND_AxRequestContext.Current;
            if (ctx == null || ctx.AxInstance == null)
                return;

            _sessionGuard.SafeLogoffAndDispose(ctx.AxInstance, ctx, reason);
            ctx.AxInstance = null;
        }

        // ---------------------------------------------------------
        // CLEANUP
        // ---------------------------------------------------------
        // Best-effort cleanup for request scope.
        public void Dispose()
        {
            try
            {
                EndRequestScope();
            }
            catch
            {
                // best effort
            }
        }
    }
}
