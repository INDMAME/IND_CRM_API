using AxaptaCOMConnector;
using IND_CRM_API.Services.Interfaces;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading; // Necesario para CancellationToken
using System.Threading.Tasks;

namespace IND_CRM_API.Services
{
    // Servicio encargado de gestionar sesiones activas contra Axapta 3.0
    // AÑADIDO: IDisposable para limpieza al cerrar la APP
    public class AxaptaSessionManager : IDisposable, IAxaptaSessionManager
    {
        // Niveles de severidad para logging
        public enum LogLevel { Info, Warning, Error }

        // Info interna de cada sesion
        private class SessionInfo
        {
            public Axapta2Class AxInstance { get; set; }
            public DateTime Expiration { get; set; }
        }

        // Diccionarios de sesiones y tokens
        private readonly ConcurrentDictionary<string, SessionInfo> _sessionsByUser = new ConcurrentDictionary<string, SessionInfo>();
        private readonly ConcurrentDictionary<string, string> _tokenToUser = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, string> _passwordByUser = new ConcurrentDictionary<string, string>();

        // AÑADIDO: Token para cancelar la tarea de fondo al cerrar la app
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        // Logger inyectado (por ahora FileAxLogger, pero facilmente sustituible)
        private static IAxLogger _logger = new FileAxLogger();

        // Configuracion de Axapta
        private readonly string _configPath = ConfigurationManager.AppSettings["AxConfigFile"];
        private readonly string _defaultUser = ConfigurationManager.AppSettings["Axapta.User"];
        private readonly string _defaultPass = ConfigurationManager.AppSettings["Axapta.Password"];
        private readonly bool _verbose = bool.TryParse(ConfigurationManager.AppSettings["Axapta.VerboseLogging"], out var v) && v;
        private readonly string _verbosePath = ConfigurationManager.AppSettings["Axapta.VerboseLogPath"] ?? @"C:\INDAxaptaLogs";

        // Flag para permitir o no el uso de credenciales por defecto
        private readonly bool _allowDefaultCredentials = true;

        // Constructor
        public AxaptaSessionManager() : this(null) { }

        // Nuevo constructor inyectable
        public AxaptaSessionManager(IAxLogger logger)
        {
            if (logger != null)
                _logger = logger;

            if (_verbose && !Directory.Exists(_verbosePath))
                Directory.CreateDirectory(_verbosePath);

            var allowDefaultSetting = ConfigurationManager.AppSettings["Axapta.AllowDefaultCredentials"];
            if (!string.IsNullOrWhiteSpace(allowDefaultSetting))
            {
                bool parsed;
                if (bool.TryParse(allowDefaultSetting, out parsed))
                    _allowDefaultCredentials = parsed;
            }

            // MODIFICADO: Pasamos el token de cancelación a la tarea
            _ = Task.Run(() => CleanupExpiredTokens(_cancellationTokenSource.Token));
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
        // CREACION / SESIONES
        // ---------------------------------------------------------
        public bool CreateOrGetSession(string username, string password, JwtService.JwtTokenInfo tokenInfo)
        {
            try
            {
                if (_sessionsByUser.ContainsKey(username))
                {
                    if (tokenInfo != null)
                    {
                        _tokenToUser[tokenInfo.Token] = username;
                        _sessionsByUser[username].Expiration = tokenInfo.Expiration;
                    }
                    if (!string.IsNullOrWhiteSpace(password))
                        _passwordByUser[username] = password;
                    Log($"[SESSION-REFRESH] {username}");
                    return true;
                }

                if (string.IsNullOrWhiteSpace(_configPath) || !File.Exists(_configPath))
                    throw new FileNotFoundException("Axapta config file not found: " + _configPath);

                var user = string.IsNullOrWhiteSpace(username) ? _defaultUser : username;
                var pass = password;

                if (string.IsNullOrWhiteSpace(pass))
                {
                    if (!_allowDefaultCredentials)
                        throw new Exception("Missing Axapta password and default credentials are disabled.");

                    pass = _defaultPass;
                }

                if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
                    throw new Exception("Missing Axapta credentials.");

                var ax = new Axapta2Class();

                // Intentamos Logon
                try
                {
                    ax.Logon2(user, pass, "", "", "", "", _configPath, false, null, null);
                }
                catch
                {
                    // AÑADIDO: Si falla el logon, liberar inmediatamente de forma segura
                    SafeReleaseCom(ax);
                    throw;
                }

                var info = new SessionInfo { AxInstance = ax, Expiration = tokenInfo?.Expiration ?? DateTime.UtcNow.AddHours(1) };

                if (!_sessionsByUser.TryAdd(username, info))
                {
                    // MODIFICADO: Uso de helper seguro para liberar si falla el añadir
                    SafeLogoffAndRelease(ax);
                    return false;
                }

                if (tokenInfo != null)
                    _tokenToUser[tokenInfo.Token] = username;
                if (!string.IsNullOrWhiteSpace(pass))
                    _passwordByUser[username] = pass;

                Log($"[SESSION-NEW] {username} cfg={_configPath}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"[ERROR-SESSION] {username} -> {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        // ---------------------------------------------------------
        // LLAMADAS A AXAPTA POR TOKEN
        // ---------------------------------------------------------
        public string CallMethodByToken(string token, string className, string methodName, object args = null)
        {
            if (!_tokenToUser.TryGetValue(token, out var username))
                throw new Exception("Invalid token.");

            if (!_sessionsByUser.TryGetValue(username, out var sess))
                throw new Exception("No Axapta session for user.");

            object result = args == null
                ? sess.AxInstance.CallStaticClassMethod(className, methodName)
                : sess.AxInstance.CallStaticClassMethod(className, methodName, args);

            return result?.ToString() ?? string.Empty;
        }

        // ---------------------------------------------------------
        // LIMPIEZA Y CIERRE
        // ---------------------------------------------------------
        // MODIFICADO: Acepta Token de cancelación
        private async Task CleanupExpiredTokens(CancellationToken token)
        {
            // MODIFICADO: Bucle condicionado al token
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;

                    foreach (var kvp in _sessionsByUser.ToArray())
                    {
                        if (kvp.Value.Expiration <= now)
                        {
                            LogoutByUser(kvp.Key);
                            Log($"[SESSION-EXPIRE] {kvp.Key} -> Token expirado, sesion cerrada automaticamente", LogLevel.Warning);
                        }
                    }

                    // MODIFICADO: Delay cancelable
                    await Task.Delay(TimeSpan.FromMinutes(2), token);
                }
                catch (TaskCanceledException)
                {
                    // Salida limpia si se cancela la tarea
                    break;
                }
                catch (Exception ex)
                {
                    Log($"[ERROR-CLEANUP] {ex.Message}", LogLevel.Error);
                    // Pequeña espera si hay error para no saturar log, también cancelable
                    try { await Task.Delay(TimeSpan.FromMinutes(1), token); } catch { break; }
                }
            }
        }

        private void LogoutByUser(string username)
        {
            if (_sessionsByUser.TryRemove(username, out var session))
            {
                // MODIFICADO: Uso de helper seguro en lugar de try/catch manual
                SafeLogoffAndRelease(session.AxInstance);

                var token = _tokenToUser.FirstOrDefault(x => x.Value == username).Key;
                if (token != null)
                    _tokenToUser.TryRemove(token, out _);

                Log($"[SESSION-CLOSE] {username} -> Sesion cerrada correctamente");
            }
        }

        // ---------------------------------------------------------
        // OBTENER INSTANCIA POR USUARIO
        // ---------------------------------------------------------
        public Axapta2Class GetAxInstanceForUser(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new Exception("Usuario no valido.");

            if (!_sessionsByUser.TryGetValue(username, out var session) || session == null)
            {
                Log($"[SESSION-MISS] No existe sesion para {username}. Intentando reconectar...", LogLevel.Warning);

                if (!TryReconnect(username))
                {
                    Log($"[SESSION-FAIL] No fue posible reconectar al usuario {username}", LogLevel.Error);
                    throw new Exception("Sesion no encontrada para el usuario: " + username);
                }

                session = _sessionsByUser[username];
                Log($"[SESSION-RESTORED] Sesion reestablecida correctamente para {username}", LogLevel.Info);
            }

            if (session.AxInstance == null)
            {
                Log($"[SESSION-INVALID] AxInstance NULL para {username}", LogLevel.Error);
                throw new Exception("Instancia de Axapta no valida para el usuario: " + username);
            }

            Log($"[SESSION-OK] Sesion valida encontrada para {username}", LogLevel.Info);

            return session.AxInstance;
        }

        // ---------------------------------------------------------
        // LLAMADA POR USUARIO (CON REINTENTO)
        // ---------------------------------------------------------
        public object CallMethodByUser(string username, string className, string methodName, object args = null)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new Exception("Usuario no valido.");

            if (!_sessionsByUser.TryGetValue(username, out var session) || session == null)
            {
                if (!TryReconnect(username))
                    throw new Exception("Sesion de Axapta no encontrada y no fue posible reconectarla.");

                session = _sessionsByUser[username];
            }

            try
            {
                Log($"[CALL-START] {username}::{className}.{methodName}", LogLevel.Info);
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                var task = Task<object>.Run(() =>
                {
                    if (args == null)
                        return session.AxInstance.CallStaticClassMethod(className, methodName);

                    if (args is object[] arr)
                        return session.AxInstance.CallStaticClassMethod(className, methodName, arr);

                    return session.AxInstance.CallStaticClassMethod(className, methodName, new object[] { args });
                });

                int timeout = 3600;
                int.TryParse(ConfigurationManager.AppSettings["Axapta.CallTimeoutSeconds"], out timeout);
                bool completed = task.Wait(TimeSpan.FromSeconds(timeout));

                stopwatch.Stop();

                if (!completed)
                {
                    Log($"[TIMEOUT] {username}::{className}.{methodName} excedio {timeout} segundos y fue cancelado.", LogLevel.Error);
                    throw new TimeoutException($"Axapta no respondio a tiempo en {className}.{methodName}. Se excedieron {timeout} segundos.");
                }

                var result = task.Result;

                Log($"[CALL-END] {username}::{className}.{methodName} completado en {stopwatch.ElapsedMilliseconds} ms. Tipo: {(result == null ? "null" : result.GetType().FullName)}",
                    LogLevel.Info);

                return result;
            }
            catch (Exception ex)
            {
                Log($"[ERROR-CALL] {username}::{className}.{methodName} -> {ex.Message}", LogLevel.Error);

                if (ex.Message.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    ex.Message.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    ex.Message.IndexOf("expired", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Log($"[AUTO-RELOGIN] {username} -> Reintentando conexion...", LogLevel.Warning);

                    try
                    {
                        LogoutByUser(username);

                        if (!TryReconnect(username))
                            throw new Exception("No se pudo restablecer la sesion Axapta.");

                        session = _sessionsByUser[username];

                        if (args == null)
                            return session.AxInstance.CallStaticClassMethod(className, methodName);

                        if (args is object[] arr)
                            return session.AxInstance.CallStaticClassMethod(className, methodName, arr);

                        return session.AxInstance.CallStaticClassMethod(className, methodName, new object[] { args });
                    }
                    catch (Exception rex)
                    {
                        Log($"[AUTO-RELOGIN-FAIL] {username} -> {rex.Message}", LogLevel.Error);
                        throw;
                    }
                }

                throw;
            }
        }

        /// <summary>
        /// Ejecuta un método estático de Axapta que devuelve un contenedor.
        /// </summary>
        public IAxaptaContainer CallContainerMethodByUser(string username, string className, string methodName, object[] args = null)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new Exception("Usuario no valido.");

            if (!_sessionsByUser.TryGetValue(username, out var session) || session == null)
            {
                if (!TryReconnect(username))
                    throw new Exception("Sesion de Axapta no encontrada y no fue posible reconectarla.");

                session = _sessionsByUser[username];
            }

            try
            {
                Log($"[CALL-START] {username}::{className}.{methodName} (CONTAINER)", LogLevel.Info);

                object raw =
                    args == null
                    ? session.AxInstance.CallStaticClassMethod(className, methodName)
                    : session.AxInstance.CallStaticClassMethod(className, methodName, args);

                var container = raw as IAxaptaContainer;

                if (container == null)
                    throw new Exception("El metodo no devolvio un AxaptaContainer valido.");

                Log($"[CALL-END] Container length={container.Length()}", LogLevel.Info);
                return container;
            }
            catch (Exception ex)
            {
                Log($"[ERROR-CONTAINER-CALL] {username}::{className}.{methodName} -> {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        // ---------------------------------------------------------
        // RECONEXION
        // ---------------------------------------------------------
        private bool TryReconnect(string username)
        {
            try
            {
                var user = string.IsNullOrWhiteSpace(username) ? _defaultUser : username;
                _passwordByUser.TryGetValue(user, out var pass);
                if (string.IsNullOrWhiteSpace(pass))
                    pass = _defaultPass;

                if (string.IsNullOrWhiteSpace(pass) && !_allowDefaultCredentials)
                {
                    Log($"[RECONNECT-FAIL] Default credentials disabled and no password available for {username}", LogLevel.Warning);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
                {
                    Log($"[RECONNECT-FAIL] Credenciales vacias para {username}", LogLevel.Warning);
                    return false;
                }

                var ax = new Axapta2Class();
                // Bloque seguro en reconexión también
                try
                {
                    ax.Logon2(user, pass, "", "", "", "", _configPath, false, null, null);
                }
                catch
                {
                    SafeReleaseCom(ax);
                    throw;
                }

                var sessionInfo = new SessionInfo
                {
                    AxInstance = ax,
                    Expiration = DateTime.UtcNow.AddHours(1)
                };

                _sessionsByUser[user] = sessionInfo;
                Log($"[SESSION-RECONNECT] Sesion restaurada para {user}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"[RECONNECT-ERROR] {username} -> {ex.Message}", LogLevel.Error);
                return false;
            }
        }


        // Metodo para refrescar la informacion del token de una sesion existente.
        // No vuelve a hacer logon en Axapta. Solo actualiza la fecha de expiracion
        // y la relacion token -> usuario en los diccionarios internos.
        public bool RefreshSessionToken(string username, JwtService.JwtTokenInfo tokenInfo, string oldToken)
        {
            // Buscar la sesion actual para el usuario
            if (!_sessionsByUser.TryGetValue(username, out var session) || session == null)
                return false;

            // Validar que el oldToken pertenece al mismo usuario si se proporciono
            if (!string.IsNullOrEmpty(oldToken) &&
                _tokenToUser.TryGetValue(oldToken, out var mappedUser) &&
                !string.Equals(mappedUser, username, StringComparison.OrdinalIgnoreCase))
            {
                return false; // token no pertenece al usuario autenticado
            }

            // Actualizar la fecha de expiracion de la sesion en memoria
            session.Expiration = tokenInfo.Expiration;

            // Si teniamos el token antiguo guardado, lo eliminamos del diccionario
            if (!string.IsNullOrEmpty(oldToken))
            {
                _tokenToUser.TryRemove(oldToken, out _);
            }

            // Registramos el nuevo token asociado al mismo usuario
            _tokenToUser[tokenInfo.Token] = username;

            // Log de control
            Log($"[SESSION-REFRESH-TOKEN] {username}", LogLevel.Info);

            return true;
        }

        // ---------------------------------------------------------
        // MEJORA: IMPLEMENTACIÓN IDISPOSABLE (GLOBAL SHUTDOWN)
        // ---------------------------------------------------------
        public void Dispose()
        {
            // 1. Detener tarea de fondo
            _cancellationTokenSource.Cancel();

            // 2. Limpiar TODAS las sesiones activas
            Log("[SHUTDOWN] Cerrando todas las sesiones COM activas...");
            foreach (var username in _sessionsByUser.Keys.ToArray())
            {
                LogoutByUser(username);
            }

            Log("[SHUTDOWN] Limpieza completada.");
        }

        // ---------------------------------------------------------
        // MEJORA: HELPERS SEGUROS COM
        // ---------------------------------------------------------
        private void SafeLogoffAndRelease(Axapta2Class ax)
        {
            if (ax == null) return;
            try
            {
                ax.Logoff();
            }
            catch { /* Ignorar errores de red al cerrar */ }
            finally
            {
                SafeReleaseCom(ax);
            }
        }

        private void SafeReleaseCom(object obj)
        {
            if (obj != null && Marshal.IsComObject(obj))
            {
                // Bucle para liberar TODAS las referencias, no solo una.
                // Esto es crucial en Singleton donde pueden quedar referencias colgadas.
                while (Marshal.ReleaseComObject(obj) > 0) { }
            }
        }
    }
}

