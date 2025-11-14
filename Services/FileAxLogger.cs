using System;
using System.Configuration;
using System.IO;
using System.Text;

namespace IND_CRM_API.Services
{
    // Logger por defecto que escribe en fichero, consola y EventLog
    public class FileAxLogger : IAxLogger
    {
        private readonly string _logPath;
        private readonly AxaptaSessionManager.LogLevel _minLogLevel;

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

                string file = Path.Combine(_logPath, $"AxaptaAudit_{DateTime.Now:yyyyMMdd}.log");

                string prefix;
                if (level == AxaptaSessionManager.LogLevel.Error) prefix = "[ERROR]";
                else if (level == AxaptaSessionManager.LogLevel.Warning) prefix = "[WARN]";
                else prefix = "[INFO]";

                string line = $"{DateTime.Now:HH:mm:ss} {prefix} {message}{Environment.NewLine}";
                File.AppendAllText(file, line, Encoding.UTF8);

                Console.WriteLine(line.Trim());

                try
                {
                    const string source = "IND_CRM_API";
                    if (!System.Diagnostics.EventLog.SourceExists(source))
                        System.Diagnostics.EventLog.CreateEventSource(source, "Application");

                    var entryType =
                        (level == AxaptaSessionManager.LogLevel.Error) ? System.Diagnostics.EventLogEntryType.Error :
                        (level == AxaptaSessionManager.LogLevel.Warning) ? System.Diagnostics.EventLogEntryType.Warning :
                        System.Diagnostics.EventLogEntryType.Information;

                    System.Diagnostics.EventLog.WriteEntry(source, message, entryType);
                }
                catch
                {
                    // Ignorar errores del EventLog
                }
            }
            catch
            {
                // Ignorar errores de escritura en fichero
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
