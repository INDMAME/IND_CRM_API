using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace IND_CRM_API.Services
{
    // Logger por defecto que escribe en fichero, consola y EventLog
    public class FileAxLogger : IAxLogger
    {
        private const string EventSourceName = "IND_CRM_API";

        private static readonly object _fileSync = new object();
        private static readonly object _eventSourceSync = new object();

        private static StreamWriter _sharedWriter;
        private static string _sharedWriterFile;
        private static bool _eventSourceChecked;
        private static bool _eventSourceEnabled;

        private readonly string _logPath;
        private readonly AxaptaSessionManager.LogLevel _minLogLevel;

        static FileAxLogger()
        {
            AppDomain.CurrentDomain.ProcessExit += OnAppDomainUnload;
            AppDomain.CurrentDomain.DomainUnload += OnAppDomainUnload;
        }

        public FileAxLogger()
        {
            _logPath = GetLogPath();

            string levelSetting = ConfigurationManager.AppSettings["LogLevel"] ?? "Info";
            if (!Enum.TryParse(levelSetting, true, out AxaptaSessionManager.LogLevel parsed))
                parsed = AxaptaSessionManager.LogLevel.Info;

            _minLogLevel = parsed;
        }

        public void Log(string message, AxaptaSessionManager.LogLevel level = AxaptaSessionManager.LogLevel.Info)
        {
            try
            {
                if (level < _minLogLevel)
                    return;

                string prefix;
                if (level == AxaptaSessionManager.LogLevel.Error) prefix = "[ERROR]";
                else if (level == AxaptaSessionManager.LogLevel.Warning) prefix = "[WARN]";
                else prefix = "[INFO]";

                var safeMessage = message ?? string.Empty;
                var line = $"{DateTime.Now:HH:mm:ss} {prefix} {safeMessage}";

                WriteFileLine(line, _logPath);
                Console.WriteLine(line);
                WriteEventLog(safeMessage, level);
            }
            catch
            {
                // Ignorar errores de escritura en fichero
            }
        }

        private static void WriteFileLine(string line, string logPath)
        {
            try
            {
                lock (_fileSync)
                {
                    EnsureWriter(logPath);
                    if (_sharedWriter == null)
                        return;

                    _sharedWriter.WriteLine(line);
                }
            }
            catch
            {
                // Ignorar errores de escritura en fichero.
            }
        }

        private static void EnsureWriter(string logPath)
        {
            var targetPath = string.IsNullOrWhiteSpace(logPath) ? @"C:\INDAxaptaLogs\" : logPath;
            if (!Directory.Exists(targetPath))
                Directory.CreateDirectory(targetPath);

            var targetFile = Path.Combine(targetPath, $"AxaptaAudit_{DateTime.Now:yyyyMMdd}.log");
            if (_sharedWriter != null && string.Equals(_sharedWriterFile, targetFile, StringComparison.OrdinalIgnoreCase))
                return;

            CloseWriterUnsafe();

            var stream = new FileStream(targetFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _sharedWriter = new StreamWriter(stream, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            _sharedWriterFile = targetFile;
        }

        private static void WriteEventLog(string message, AxaptaSessionManager.LogLevel level)
        {
            try
            {
                if (!EnsureEventSource())
                    return;

                var entryType =
                    (level == AxaptaSessionManager.LogLevel.Error) ? EventLogEntryType.Error :
                    (level == AxaptaSessionManager.LogLevel.Warning) ? EventLogEntryType.Warning :
                    EventLogEntryType.Information;

                EventLog.WriteEntry(EventSourceName, message, entryType);
            }
            catch
            {
                // Ignorar errores del EventLog.
            }
        }

        private static bool EnsureEventSource()
        {
            if (_eventSourceChecked)
                return _eventSourceEnabled;

            lock (_eventSourceSync)
            {
                if (_eventSourceChecked)
                    return _eventSourceEnabled;

                try
                {
                    if (!EventLog.SourceExists(EventSourceName))
                        EventLog.CreateEventSource(EventSourceName, "Application");

                    _eventSourceEnabled = true;
                }
                catch
                {
                    _eventSourceEnabled = false;
                }
                finally
                {
                    _eventSourceChecked = true;
                }
            }

            return _eventSourceEnabled;
        }

        private static void OnAppDomainUnload(object sender, EventArgs args)
        {
            lock (_fileSync)
            {
                CloseWriterUnsafe();
            }
        }

        private static void CloseWriterUnsafe()
        {
            try
            {
                _sharedWriter?.Flush();
                _sharedWriter?.Dispose();
            }
            catch
            {
                // Ignorar errores al cerrar.
            }
            finally
            {
                _sharedWriter = null;
                _sharedWriterFile = null;
            }
        }

        private static string GetLogPath()
        {
            try
            {
                var configured = ConfigurationManager.AppSettings["LogPath"];
                if (!string.IsNullOrWhiteSpace(configured))
                {
                    if (!Directory.Exists(configured))
                        Directory.CreateDirectory(configured);
                    return configured;
                }
            }
            catch
            {
            }

            string defaultPath = @"C:\INDAxaptaLogs\";
            if (!Directory.Exists(defaultPath))
                Directory.CreateDirectory(defaultPath);
            return defaultPath;
        }
    }
}
