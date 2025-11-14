using AxaptaCOMConnector;
using System;
using System.Collections.Concurrent;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace IND_CRM_APIs.Services
{
    /// <summary>
    /// Servicio encargado de gestionar sesiones activas contra Axapta 3.0 (Business Connector COM).
    /// Permite crear, reutilizar y cerrar sesiones por usuario, controlando el tiempo de expiración
    /// sincronizado con los tokens JWT emitidos por la API.
    /// Incluye mecanismos de reconexión automática, logging y limpieza de sesiones inactivas.
    /// </summary>
    public class AxaptaSessionManager
    {
        /// Niveles de severidad utilizados en los registros del sistema.
        public enum LogLevel
        {
            Info,
            Warning,
            Error
        }

   


        /// Estructura interna que contiene información de cada sesión activa.
        private class SessionInfo
        {
            public Axapta2Class AxInstance { get; set; }
            public DateTime Expiration { get; set; }
        }

        // ========================= CAMPOS INTERNOS ========================= //

        private readonly ConcurrentDictionary<string, SessionInfo> _sessionsByUser = new ConcurrentDictionary<string, SessionInfo>();
        private readonly ConcurrentDictionary<string, string> _tokenToUser = new ConcurrentDictionary<string, string>();

        private static readonly string _logPath = GetLogPath();
        private readonly string _configPath = System.Configuration.ConfigurationManager.AppSettings["AxConfigFile"];
        private readonly string _defaultUser = System.Configuration.ConfigurationManager.AppSettings["Axapta.User"];
        private readonly string _defaultPass = System.Configuration.ConfigurationManager.AppSettings["Axapta.Password"];
        private readonly bool _verbose = bool.TryParse(System.Configuration.ConfigurationManager.AppSettings["Axapta.VerboseLogging"], out var v) && v;
        private readonly string _verbosePath = System.Configuration.ConfigurationManager.AppSettings["Axapta.VerboseLogPath"] ?? @"C:\INDAxaptaLogs";

        private static readonly string _logLevelSetting = System.Configuration.ConfigurationManager.AppSettings["LogLevel"] ?? "Info";
        private static readonly LogLevel _minLogLevel = Enum.TryParse(_logLevelSetting, true, out LogLevel parsed) ? parsed : LogLevel.Info;
        private static bool ShouldLog(LogLevel level) => level >= _minLogLevel;

        // ========================= CONSTRUCTOR ========================= //

        /// <summary>
        /// Inicializa el gestor de sesiones y crea el directorio de logs si es necesario.
        /// Lanza una tarea en segundo plano para limpiar sesiones expiradas periódicamente.
        /// </summary>
        public AxaptaSessionManager()
        {
            if (_verbose && !Directory.Exists(_verbosePath))
                Directory.CreateDirectory(_verbosePath);

            _ = Task.Run(CleanupExpiredTokens);
        }


        // ========================= CREACIÓN / SESIONES ========================= //


        /// <summary>
        /// Crea una nueva sesión de Axapta para el usuario especificado, o reutiliza una existente.
        /// Asocia el token JWT con el usuario para llamadas posteriores.
        /// </summary>
        /// <param name="username">Nombre de usuario de Axapta.</param>
        /// <param name="password">Contraseña de Axapta.</param>
        /// <param name="tokenInfo">Información del token JWT emitido (token + expiración).</param>
        /// <returns>True si la sesión se creó o actualizó correctamente.</returns>
        public bool CreateOrGetSession(string username, string password, JwtService.JwtTokenInfo tokenInfo)
        {
            try
            {
                if (_sessionsByUser.ContainsKey(username))
                {
                    _tokenToUser[tokenInfo.Token] = username;
                    _sessionsByUser[username].Expiration = tokenInfo.Expiration;
                    Log($"[SESSION-REFRESH] {username}");
                    return true;
                }

                if (string.IsNullOrWhiteSpace(_configPath) || !File.Exists(_configPath))
                    throw new FileNotFoundException("Axapta config file not found: " + _configPath);

                var user = string.IsNullOrWhiteSpace(username) ? _defaultUser : username;
                var pass = string.IsNullOrWhiteSpace(password) ? _defaultPass : password;
                if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
                    throw new Exception("Missing Axapta credentials.");

                var ax = new Axapta2Class();
                ax.Logon2(user, pass, "", "", "", "", _configPath, false, null, null);

                var info = new SessionInfo { AxInstance = ax, Expiration = tokenInfo.Expiration };
                if (!_sessionsByUser.TryAdd(username, info))
                {
                    try { ax.Logoff(); } catch { }
                    Marshal.ReleaseComObject(ax);
                    return false;
                }

                _tokenToUser[tokenInfo.Token] = username;
                Log($"[SESSION-NEW] {username} cfg={_configPath}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"[ERROR-SESSION] {username} -> {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        // ========================= LLAMADAS A AXAPTA =========================

        /// <summary>
        /// Ejecuta un método de clase estática de Axapta a partir de un token JWT.
        /// </summary>
        /// <param name="token">Token JWT del usuario autenticado.</param>
        /// <param name="className">Nombre de la clase X++ en Axapta.</param>
        /// <param name="methodName">Nombre del método dentro de esa clase.</param>
        /// <param name="args">Parámetros opcionales a pasar al método.</param>
        /// <returns>Resultado devuelto por Axapta en formato texto.</returns>
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


        // ========================= LOGS =========================

        /// <summary>
        /// Obtiene la ruta del archivo de log, creando el directorio si no existe.
        /// </summary>
        private static string GetLogPath()
        {
            try
            {
                var configured = System.Configuration.ConfigurationManager.AppSettings["LogPath"];
                if (!string.IsNullOrWhiteSpace(configured))
                {
                    if (!System.IO.Directory.Exists(configured))
                        System.IO.Directory.CreateDirectory(configured);
                    return configured;
                }
            }
            catch { }

            string defaultPath = @"C:\INDAxaptaLogs\";
            if (!System.IO.Directory.Exists(defaultPath))
                System.IO.Directory.CreateDirectory(defaultPath);
            return defaultPath;
        }


        public static void LogStatic(string message)
        {
            string file = System.IO.Path.Combine(_logPath, $"AxaptaAudit_{DateTime.Now:yyyyMMdd}.log");
            string line = $"{DateTime.Now:HH:mm:ss} [API] {message}{Environment.NewLine}";
            System.IO.File.AppendAllText(file, line, System.Text.Encoding.UTF8);
            Console.WriteLine(line.Trim());
        }

        /// <summary>
        /// Escribe una línea de log en archivo, consola y (si es posible) en el visor de eventos de Windows.
        /// </summary>
        /// <param name="message">Mensaje a registrar.</param>
        /// <param name="level">Nivel de severidad del log (por defecto Info).</param>
        private static void Log(string message, LogLevel level = LogLevel.Info)
        {
            try
            {
                if (!ShouldLog(level))
                    return;

                string file = System.IO.Path.Combine(_logPath, $"AxaptaAudit_{DateTime.Now:yyyyMMdd}.log");

                string prefix;
                if (level == LogLevel.Error) prefix = "[ERROR]";
                else if (level == LogLevel.Warning) prefix = "[WARN]";
                else prefix = "[INFO]";

                string line = $"{DateTime.Now:HH:mm:ss} {prefix} {message}{Environment.NewLine}";
                System.IO.File.AppendAllText(file, line, System.Text.Encoding.UTF8);

                // Consola
                Console.WriteLine(line.Trim());

                // Event Viewer (Application)
                try
                {
                    const string source = "IND_CRM_APIs";
                    if (!System.Diagnostics.EventLog.SourceExists(source))
                        System.Diagnostics.EventLog.CreateEventSource(source, "Application");

                    var entryType =
                        (level == LogLevel.Error) ? System.Diagnostics.EventLogEntryType.Error :
                        (level == LogLevel.Warning) ? System.Diagnostics.EventLogEntryType.Warning :
                        System.Diagnostics.EventLogEntryType.Information;

                    System.Diagnostics.EventLog.WriteEntry(source, message, entryType);
                }
                catch
                {
                    // Ignorar si no hay permisos para crear/usar la fuente
                }
            }
            catch { }
        }


        // ========================= LIMPIEZA Y CIERRE =========================

        /// <summary>
        /// Hilo en segundo plano que cierra sesiones cuya fecha de expiración haya pasado.
        /// Ejecuta una verificación periódica cada 2 minutos.
        /// </summary>
        private async Task CleanupExpiredTokens()
        {
            while (true)
            {
                try
                {
                    var now = DateTime.UtcNow;

                    foreach (var kvp in _sessionsByUser.ToArray())
                    {
                        if (kvp.Value.Expiration <= now)
                        {
                            // Cierra sesión por expiración sincronizada con token
                            LogoutByUser(kvp.Key);
                            Log($"[SESSION-EXPIRE] {kvp.Key} -> Token expirado, sesión cerrada automáticamente", LogLevel.Warning);
                        }
                    }

                    await Task.Delay(TimeSpan.FromMinutes(2)); // verificación periódica
                }
                catch (Exception ex)
                {
                    Log($"[ERROR-CLEANUP] {ex.Message}", LogLevel.Error);
                    await Task.Delay(TimeSpan.FromMinutes(1));
                }
            }
        }

        /// <summary>
        /// Cierra manualmente la sesión de un usuario, eliminándola del registro activo.
        /// </summary>
        /// <param name="username">Usuario cuya sesión será finalizada.</param>
        private void LogoutByUser(string username)
        {
            if (_sessionsByUser.TryRemove(username, out var session))
            {
                try { session.AxInstance.Logoff(); } catch { }
                try { Marshal.ReleaseComObject(session.AxInstance); } catch { }

                var token = _tokenToUser.FirstOrDefault(x => x.Value == username).Key;
                if (token != null)
                    _tokenToUser.TryRemove(token, out _);

                Log($"[SESSION-CLOSE] {username} -> Sesión cerrada correctamente");
            }
        }


        public Axapta2Class GetAxInstanceForUser(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new Exception("Usuario no válido.");

            // Buscar sesión activa
            if (!_sessionsByUser.TryGetValue(username, out var session) || session == null)
            {
                Log($"[SESSION-MISS] No existe sesión para {username}. Intentando reconectar...", LogLevel.Warning);

                // Intentar reconectar automáticamente
                if (!TryReconnect(username))
                {
                    Log($"[SESSION-FAIL] No fue posible reconectar al usuario {username}", LogLevel.Error);
                    throw new Exception("Sesión no encontrada para el usuario: " + username);
                }

                session = _sessionsByUser[username];

                Log($"[SESSION-RESTORED] Sesión reestablecida correctamente para {username}", LogLevel.Info);
            }

            if (session.AxInstance == null)
            {
                Log($"[SESSION-INVALID] AxInstance NULL para {username}", LogLevel.Error);
                throw new Exception("Instancia de Axapta no válida para el usuario: " + username);
            }

            // Debug de sesión válida
            Log($"[SESSION-OK] Sesión válida encontrada para {username}", LogLevel.Info);

            return session.AxInstance;
        }


        /// <summary>
        /// Ejecuta un método de clase estática en Axapta usando el nombre del usuario.
        /// Si la sesión no existe o expiró, intenta reconectarla automáticamente.
        /// </summary>
        /// <param name="username">Usuario asociado a la sesión Axapta.</param>
        /// <param name="className">Nombre de la clase X++ en Axapta.</param>
        /// <param name="methodName">Nombre del método dentro de la clase.</param>
        /// <param name="args">Parámetros opcionales.</param>
        /// <returns>Resultado en formato string.</returns>
        /// <exception cref="Exception">Se lanza si la sesión no se puede recuperar o si la llamada falla.</exception>
        public object CallMethodByUser(string username, string className, string methodName, object args = null)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new Exception("Usuario no válido.");

            if (!_sessionsByUser.TryGetValue(username, out var session) || session == null)
            {
                if (!TryReconnect(username))
                    throw new Exception("Sesión de Axapta no encontrada y no fue posible reconectarla.");

                session = _sessionsByUser[username];
            }

            try
            {
                Log($"[CALL-START] {username}::{className}.{methodName}", LogLevel.Info);
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                // Ejecutamos la llamada COM en un hilo separado, con posibilidad de timeout
                var task = Task<object>.Run(() =>
                {
                    if (args == null)
                        return session.AxInstance.CallStaticClassMethod(className, methodName);

                    if (args is object[] arr)
                        return session.AxInstance.CallStaticClassMethod(className, methodName, arr);

                    return session.AxInstance.CallStaticClassMethod(className, methodName, new object[] { args });
                });

                // Timeout aumentado a 600 segundos
                int timeout = 600;
                int.TryParse(ConfigurationManager.AppSettings["Axapta.CallTimeoutSeconds"], out timeout);
                bool completed = task.Wait(TimeSpan.FromSeconds(timeout));


                stopwatch.Stop();

                if (!completed)
                {
                    Log($"[TIMEOUT] {username}::{className}.{methodName} excedió 600 segundos y fue cancelado.", LogLevel.Error);
                    throw new TimeoutException($"Axapta no respondió a tiempo en {className}.{methodName}. Se excedieron 600 segundos.");
                }

                var result = task.Result;

                Log($"[CALL-END] {username}::{className}.{methodName} completado en {stopwatch.ElapsedMilliseconds} ms. Tipo: {(result == null ? "null" : result.GetType().FullName)}",
                    LogLevel.Info);

                return result;
            }
            catch (Exception ex)
            {
                Log($"[ERROR-CALL] {username}::{className}.{methodName} -> {ex.Message}", LogLevel.Error);

                // Intento de reconexión automática si la sesión de Axapta caducó
                if (ex.Message.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    ex.Message.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    ex.Message.IndexOf("expired", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Log($"[AUTO-RELOGIN] {username} -> Reintentando conexión...", LogLevel.Warning);

                    try
                    {
                        LogoutByUser(username);

                        if (!TryReconnect(username))
                            throw new Exception("No se pudo restablecer la sesión Axapta.");

                        session = _sessionsByUser[username];

                        // Reintento después de reconectar
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

    


        /*public object CallMethodByUser(string username, string className, string methodName, object args = null)
        {
            Log($"[DEBUG] {username} -> Axapta call start: {className}.{methodName}", LogLevel.Info);
            var watch = System.Diagnostics.Stopwatch.StartNew();


            if (string.IsNullOrWhiteSpace(username))
                throw new Exception("Usuario no válido.");

            if (!_sessionsByUser.TryGetValue(username, out var session) || session == null)
            {
                // 🔁 Reconexión si no existe sesión
                if (!TryReconnect(username))
                    throw new Exception("Sesión de Axapta no encontrada y no fue posible reconectarla.");

                session = _sessionsByUser[username];
            }

            try
            {
                object result;

                // Llamada a método estático
                if (args == null)
                    result = session.AxInstance.CallStaticClassMethod(className, methodName);
                else if (args is object[] arr)
                    result = session.AxInstance.CallStaticClassMethod(className, methodName, arr);
                else
                    result = session.AxInstance.CallStaticClassMethod(className, methodName, new object[] { args });

                // Log auxiliar para saber qué tipo devuelve AX
                Log($"[CALL] {username}::{className}.{methodName} -> Tipo de retorno: {(result == null ? "null" : result.GetType().FullName)}", LogLevel.Info);
                
                watch.Stop();
                Log($"[DEBUG] {username} -> Axapta call end ({watch.ElapsedMilliseconds} ms)", LogLevel.Info);

                return result;
            }
            catch (Exception ex)
            {
                Log($"[ERROR-CALL] {username}::{className}.{methodName} -> {ex.Message}", LogLevel.Error);

                // 🧩 Auto-relogin si la sesión ha caducado
                if (ex.Message != null &&
                    (ex.Message.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     ex.Message.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     ex.Message.IndexOf("expired", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    Log($"[AUTO-RELOGIN] {username} -> reintentando conexión...", LogLevel.Warning);

                    try
                    {
                        LogoutByUser(username);

                        if (!TryReconnect(username))
                            throw new Exception("No se pudo restablecer la sesión Axapta.");

                        session = _sessionsByUser[username];

                        object retryResult;
                        if (args == null)
                            retryResult = session.AxInstance.CallStaticClassMethod(className, methodName);
                        else if (args is object[] arr)
                            retryResult = session.AxInstance.CallStaticClassMethod(className, methodName, arr);
                        else
                            retryResult = session.AxInstance.CallStaticClassMethod(className, methodName, new object[] { args });

                        Log($"[AUTO-RELOGIN] Reintento exitoso para {username}", LogLevel.Info);
                        return retryResult;
                    }
                    catch (Exception rex)
                    {
                        Log($"[AUTO-RELOGIN-FAIL] {username} -> {rex.Message}", LogLevel.Error);
                        throw new Exception("No se pudo reconectar automáticamente con Axapta: " + rex.Message);
                    }
                }

                throw;
            }
        }*/




        // ========================= RECONEXIÓN =========================

        /// <summary>
        /// Intenta restablecer una sesión de Axapta para el usuario indicado.
        /// </summary>
        /// <param name="username">Usuario cuya sesión se intentará restaurar.</param>
        /// <returns>True si la reconexión fue exitosa; false en caso contrario.</returns>
        private bool TryReconnect(string username)
        {
            try
            {
                var user = string.IsNullOrWhiteSpace(username) ? _defaultUser : username;
                var pass = _defaultPass;

                if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
                {
                    Log($"[RECONNECT-FAIL] Credenciales vacías para {username}", LogLevel.Warning);
                    return false;
                }

                var ax = new Axapta2Class();
                ax.Logon2(user, pass, "", "", "", "", _configPath, false, null, null);

                var sessionInfo = new SessionInfo
                {
                    AxInstance = ax,
                    Expiration = DateTime.UtcNow.AddHours(1) // fallback si no hay token asociado
                };

                _sessionsByUser[user] = sessionInfo;
                Log($"[SESSION-RECONNECT] Sesión restaurada para {user}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"[RECONNECT-ERROR] {username} -> {ex.Message}", LogLevel.Error);
                return false;
            }
        }


        public IAxaptaContainer CallContainerMethodByUser(  string username,
                                                            string className,
                                                            string methodName,
                                                            object[] args = null)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new Exception("Usuario no válido.");

            if (!_sessionsByUser.TryGetValue(username, out var session) || session == null)
            {
                if (!TryReconnect(username))
                    throw new Exception("Sesión de Axapta no encontrada y no fue posible reconectarla.");

                session = _sessionsByUser[username];
            }

            try
            {
                Log($"[CALL-START] {username}::{className}.{methodName} (CONTAINER)", LogLevel.Info);
                var sw = System.Diagnostics.Stopwatch.StartNew();

                object raw =
                    args == null
                    ? session.AxInstance.CallStaticClassMethod(className, methodName)
                    : session.AxInstance.CallStaticClassMethod(className, methodName, args);

                sw.Stop();

                if (raw == null)
                    return null;

                var container = raw as IAxaptaContainer;

                if (container == null)
                    throw new Exception("El método no devolvió un AxaptaContainer válido.");

                Log($"[CALL-END] Container length={container.Length()} en {sw.ElapsedMilliseconds} ms",
                    LogLevel.Info);

                return container;
            }
            catch (Exception ex)
            {
                Log($"[ERROR-CONTAINER-CALL] {username}::{className}.{methodName} -> {ex.Message}", LogLevel.Error);
                throw;
            }
        }




    }
}
