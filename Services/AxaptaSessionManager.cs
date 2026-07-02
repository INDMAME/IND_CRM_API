using AxaptaCOMConnector;
using IND_CRM_API.Helpers;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Threading;

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
            LogSessionTrace("begin-request-scope", null, IND_AxRequestContext.Current, "endpoint=" + endpoint + " company=" + company);
        }

        // Ends request scope and disposes the Axapta instance if any.
        public void EndRequestScope()
        {
            var ctx = IND_AxRequestContext.Current;
            if (ctx == null)
                return;

            LogSessionTrace("end-request-scope", ctx.Username, ctx, "hasComSession=" + (ctx.ComSession != null));
            try
            {
                var hadComSession = ctx.ComSession != null;
                if (hadComSession)
                    _sessionGuard.SafeLogoffAndDispose(ctx.ComSession, ctx, "request-end");

                if (hadComSession)
                    _sessionGuard.TryShutdownComPlusAfterCall(ctx, "request-end");
            }
            finally
            {
                ctx.ComSession = null;
                ctx.ComAccessLease?.Dispose();
                ctx.ComAccessLease = null;
                IND_AxRequestContext.Clear();
            }
        }

        // ---------------------------------------------------------
        // CREACION / SESIONES
        // ---------------------------------------------------------
        public bool CreateOrGetSession(string username, string password, JwtService.JwtTokenInfo tokenInfo)
        {
            var ctx = IND_AxRequestContext.Current;
            var sw = Stopwatch.StartNew();
            try
            {
                LogSessionTrace(
                    "create-or-get-session-begin",
                    username,
                    ctx,
                    $"providedUserEmpty={string.IsNullOrWhiteSpace(username)} providedPassword={(!string.IsNullOrWhiteSpace(password))} tokenInfoPresent={tokenInfo != null}",
                    durationMs: sw.ElapsedMilliseconds);

                var resolvedUser = string.IsNullOrWhiteSpace(username) ? _defaultUser : username;
                if (string.IsNullOrWhiteSpace(resolvedUser))
                {
                    LogSessionTrace("create-or-get-session-missing-user", username, ctx, null, LogLevel.Warning, durationMs: sw.ElapsedMilliseconds);
                    Log("[AUTH] Missing Axapta username.", LogLevel.Warning);
                    return false;
                }

                var resolvedPassword = ResolvePassword(resolvedUser, password, out var passwordSource);
                LogSessionTrace(
                    "create-or-get-session-password-resolved",
                    resolvedUser,
                    ctx,
                    $"passwordSource={passwordSource} passwordAvailable={!string.IsNullOrWhiteSpace(resolvedPassword)} allowDefaultCredentials={_allowDefaultCredentials}",
                    durationMs: sw.ElapsedMilliseconds);

                if (string.IsNullOrWhiteSpace(resolvedPassword))
                {
                    LogSessionTrace("create-or-get-session-missing-password", resolvedUser, ctx, "passwordSource=" + passwordSource, LogLevel.Warning, durationMs: sw.ElapsedMilliseconds);
                    Log($"[AUTH] Missing Axapta password for {resolvedUser}.", LogLevel.Warning);
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(password) && _passwordByUser.TryGetValue(resolvedUser, out var stored) &&
                    !string.Equals(stored, password, StringComparison.Ordinal))
                {
                    LogSessionTrace("create-or-get-session-password-rotation", resolvedUser, ctx, "cachedPasswordMismatch=true", LogLevel.Warning, durationMs: sw.ElapsedMilliseconds);
                    Log($"[SESSION-PASSWORD-ROTATION] {resolvedUser} -> password mismatch, forcing relogon.", LogLevel.Warning);
                }

                LogSessionTrace("create-or-get-session-before-smoke-test", resolvedUser, ctx, "passwordSource=" + passwordSource, durationMs: sw.ElapsedMilliseconds);
                var smokeOk = _sessionGuard.SmokeTest(resolvedUser, resolvedPassword, ctx);
                LogSessionTrace(
                    "create-or-get-session-after-smoke-test",
                    resolvedUser,
                    ctx,
                    "smokeOk=" + smokeOk,
                    smokeOk ? LogLevel.Info : LogLevel.Warning,
                    durationMs: sw.ElapsedMilliseconds);

                if (!smokeOk)
                {
                    Log($"[AUTH-FAIL] Axapta smoke test failed for {resolvedUser}.", LogLevel.Warning);
                    return false;
                }

                _passwordByUser[resolvedUser] = resolvedPassword;
                LogSessionTrace("create-or-get-session-password-cached", resolvedUser, ctx, "cacheUpdated=true", durationMs: sw.ElapsedMilliseconds);

                if (tokenInfo != null)
                {
                    _tokenToUser[tokenInfo.Token] = resolvedUser;
                    LogSessionTrace("create-or-get-session-token-bound", resolvedUser, ctx, "tokenBound=true", durationMs: sw.ElapsedMilliseconds);
                }

                Log($"[AUTH-OK] Axapta session validated for {resolvedUser} source={passwordSource}.", LogLevel.Info);
                LogSessionTrace("create-or-get-session-success", resolvedUser, ctx, "passwordSource=" + passwordSource, durationMs: sw.ElapsedMilliseconds);
                return true;
            }
            catch (IND_AxCallTimeoutException ex)
            {
                LogSessionTrace("create-or-get-session-timeout", username, ctx, null, LogLevel.Error, ex, sw.ElapsedMilliseconds);
                Log($"[ERROR-SESSION-TIMEOUT] {username} -> {ex.Message}", LogLevel.Error);
                throw;
            }
            catch (Exception ex)
            {
                LogSessionTrace("create-or-get-session-exception", username, ctx, null, LogLevel.Error, ex, sw.ElapsedMilliseconds);
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
            var ctx = IND_AxRequestContext.Current;
            if (string.IsNullOrWhiteSpace(username) || tokenInfo == null)
            {
                LogSessionTrace(
                    "refresh-session-token-invalid-input",
                    username,
                    ctx,
                    $"usernameMissing={string.IsNullOrWhiteSpace(username)} tokenInfoNull={tokenInfo == null}",
                    LogLevel.Warning);
                return false;
            }

            if (!string.IsNullOrEmpty(oldToken) &&
                _tokenToUser.TryGetValue(oldToken, out var mappedUser) &&
                !string.Equals(mappedUser, username, StringComparison.OrdinalIgnoreCase))
            {
                LogSessionTrace("refresh-session-token-mismatch", username, ctx, "oldTokenBelongsTo=" + mappedUser, LogLevel.Warning);
                return false; // token no pertenece al usuario autenticado
            }

            if (!string.IsNullOrEmpty(oldToken))
            {
                _tokenToUser.TryRemove(oldToken, out _);
                LogSessionTrace("refresh-session-token-old-removed", username, ctx, "oldTokenPresent=true");
            }

            _tokenToUser[tokenInfo.Token] = username;
            Log($"[SESSION-REFRESH-TOKEN] {username}", LogLevel.Info);
            LogSessionTrace("refresh-session-token-success", username, ctx, "oldTokenPresent=" + (!string.IsNullOrEmpty(oldToken)));
            return true;
        }

        // ---------------------------------------------------------
        // AX INSTANCE (PER REQUEST)
        // ---------------------------------------------------------
        // Returns per-request Axapta instance (creates on demand).
        public AxaptaComSession GetAxInstanceForUser(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new Exception("Usuario no valido.");

            var ctx = IND_AxRequestContext.Current;
            LogSessionTrace("get-ax-instance-begin", username, ctx, "hasContext=" + (ctx != null));
            if (ctx == null)
            {
                Log("[AX-SESSION] Missing request scope; creating transient context.", LogLevel.Warning);
                var fallbackCorrelationId = Guid.NewGuid().ToString("N");
                IND_AxRequestContext.Start(fallbackCorrelationId, Guid.NewGuid().ToString("N"), "unknown", string.Empty);
                ctx = IND_AxRequestContext.Current;
                LogSessionTrace("get-ax-instance-created-transient-context", username, ctx, null, LogLevel.Warning);
            }

            ctx.Username = username;

            if (ctx.ComSession != null)
            {
                if (!ctx.ComSession.Matches(username, _configPath, ctx.Company))
                {
                    LogSessionTrace("get-ax-instance-incompatible-existing-session", username, ctx, "existingUser=" + ctx.ComSession.Username + " existingCompany=" + ctx.ComSession.Company, LogLevel.Warning);
                    ResetRequestSession("incompatible-session");
                }
                else
                {
                    LogSessionTrace("get-ax-instance-reuse-existing", username, ctx, "hasComSession=true");
                    return ctx.ComSession;
                }
            }

            var resolvedPassword = ResolvePassword(username, null, out var source);
            LogSessionTrace(
                "get-ax-instance-password-resolved",
                username,
                ctx,
                $"passwordSource={source} passwordAvailable={!string.IsNullOrWhiteSpace(resolvedPassword)}");

            if (string.IsNullOrWhiteSpace(resolvedPassword))
                throw new Exception("No hay credenciales disponibles para Axapta.");

            LogSessionTrace("get-ax-instance-before-ensure-logged-on", username, ctx, "passwordSource=" + source);
            Axapta2Class axInstance = null;
            try
            {
                if (ctx.ComAccessLease == null)
                    ctx.ComAccessLease = _sessionGuard.EnterComAccess(ctx, "request-session");

                axInstance = _sessionGuard.EnsureLoggedOn(username, resolvedPassword, ctx);
                ctx.ComSession = new AxaptaComSession(axInstance, _sessionGuard, ctx, username, _configPath, ctx.Company);
                axInstance = null;
                LogSessionTrace("get-ax-instance-after-ensure-logged-on", username, ctx, "passwordSource=" + source + " axSessionCreated=" + (ctx.ComSession != null));
                Log($"[AX-SESSION] Session created user={username} source={source}.", LogLevel.Info);
                return ctx.ComSession;
            }
            catch
            {
                if (ctx.ComSession != null)
                {
                    _sessionGuard.SafeLogoffAndDispose(ctx.ComSession, ctx, "logon-failed");
                    ctx.ComSession = null;
                }
                else if (axInstance != null)
                {
                    _sessionGuard.SafeLogoffAndDispose(axInstance, ctx, "logon-failed");
                }

                ctx.ComAccessLease?.Dispose();
                ctx.ComAccessLease = null;
                throw;
            }
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
                className + "." + methodName,
                ex => RecoverBusinessConnectorBeforeRetry(ex, ctx, className + "." + methodName)
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
                className + "." + methodName,
                ex => RecoverBusinessConnectorBeforeRetry(ex, ctx, className + "." + methodName)
            );
        }

        // Low-level COM invocation helper.
        private object InvokeAxMethod(AxaptaComSession ax, string className, string methodName, object args)
        {
            if (ax == null)
                throw new Exception("Instancia Axapta no valida.");

            var ctx = IND_AxRequestContext.Current;
            var sw = Stopwatch.StartNew();
            var argsDescription = DescribeArgs(args);
            LogSessionTrace("invoke-ax-method-before-call", ctx?.Username, ctx, $"class={className} method={methodName} args={argsDescription}", durationMs: sw.ElapsedMilliseconds);

            try
            {
                object result;
                if (args == null)
                    result = ax.CallStaticClassMethod(className, methodName);
                else if (args is object[] arr)
                    result = ax.CallStaticClassMethod(className, methodName, arr);
                else
                    result = ax.CallStaticClassMethod(className, methodName, new object[] { args });

                LogSessionTrace(
                    "invoke-ax-method-after-call",
                    ctx?.Username,
                    ctx,
                    $"class={className} method={methodName} resultType={(result == null ? "null" : result.GetType().FullName)}",
                    durationMs: sw.ElapsedMilliseconds);
                return result;
            }
            catch (Exception ex)
            {
                LogSessionTrace(
                    "invoke-ax-method-exception",
                    ctx?.Username,
                    ctx,
                    $"class={className} method={methodName} args={argsDescription}",
                    LogLevel.Error,
                    ex,
                    sw.ElapsedMilliseconds);
                throw;
            }
        }

        private bool RecoverBusinessConnectorBeforeRetry(Exception ex, IND_AxRequestContext ctx, string operationName)
        {
            var activeContext = ctx ?? IND_AxRequestContext.Current;
            ResetRequestSession("retry-system-changed");
            using (_sessionGuard.EnterComAccess(activeContext, "business-connector-recovery"))
            {
                return _sessionGuard.TryRecoverBusinessConnector(activeContext, ex, operationName, "before single retry");
            }
        }

        // Disposes the current request session so it can be recreated on retry.
        private void ResetRequestSession(string reason)
        {
            var ctx = IND_AxRequestContext.Current;
            if (ctx == null || ctx.ComSession == null)
                return;

            LogSessionTrace("reset-request-session", ctx.Username, ctx, "reason=" + reason, LogLevel.Warning);
            try
            {
                _sessionGuard.SafeLogoffAndDispose(ctx.ComSession, ctx, reason);
            }
            finally
            {
                ctx.ComSession = null;
                ctx.ComAccessLease?.Dispose();
                ctx.ComAccessLease = null;
            }
        }

        // Emits detailed session traces to correlate auth stages with Axapta COM activity.
        private void LogSessionTrace(
            string stage,
            string username,
            IND_AxRequestContext ctx,
            string detail,
            LogLevel level = LogLevel.Info,
            Exception ex = null,
            long? durationMs = null)
        {
            var activeContext = ctx ?? IND_AxRequestContext.Current;
            var ctxAgeMs = activeContext == null ? 0 : (long)Math.Max(0, (DateTime.UtcNow - activeContext.StartedUtc).TotalMilliseconds);
            var parts = new List<string>
            {
                "[AX-SESSION-TRACE]",
                "component=AxaptaSessionManager",
                "stage=" + (stage ?? string.Empty),
                "correlationId=" + (activeContext?.CorrelationId ?? "-"),
                "traceId=" + (activeContext?.TraceId ?? "-"),
                "axUser=" + (username ?? activeContext?.Username ?? string.Empty),
                "company=" + (activeContext?.Company ?? string.Empty),
                "endpoint=" + (activeContext?.Endpoint ?? string.Empty),
                "threadId=" + Environment.CurrentManagedThreadId,
                "apartment=" + Thread.CurrentThread.GetApartmentState(),
                "processId=" + Process.GetCurrentProcess().Id,
                "ctxAgeMs=" + ctxAgeMs
            };

            if (durationMs.HasValue)
                parts.Add("durationMs=" + durationMs.Value);

            if (!string.IsNullOrWhiteSpace(detail))
                parts.Add("detail=" + Truncate(detail, 3000));

            if (ex != null)
            {
                parts.Add("error=" + ex.GetType().Name);
                parts.Add("message=" + Truncate(ex.Message, 2000));
                if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                    parts.Add("stack=" + Truncate(ex.StackTrace, 4000));
            }

            _logger.Log(string.Join(" ", parts), level);
        }

        private static string DescribeArgs(object args)
        {
            if (args == null)
                return "null";

            if (args is object[] arr)
                return "array:" + arr.Length;

            return args.GetType().FullName;
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value;

            return value.Substring(0, maxLength);
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
