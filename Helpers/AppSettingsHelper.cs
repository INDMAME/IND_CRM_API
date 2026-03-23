using System;
using System.Configuration;

namespace IND_CRM_API.Helpers
{
    /// <summary>
    /// Helper para leer AppSettings con soporte de variables de entorno.
    /// </summary>
    public static class AppSettingsHelper
    {
        /// <summary>
        /// Obtiene un valor de AppSettings, priorizando la variable de entorno indicada.
        /// </summary>
        /// <param name="key">Clave de AppSettings.</param>
        /// <param name="envVarName">Nombre de la variable de entorno (opcional).</param>
        /// <returns>Valor resuelto o null si no existe.</returns>
        public static string GetSetting(string key, string envVarName = null)
        {
            if (string.IsNullOrWhiteSpace(envVarName))
                return GetSetting(key, Array.Empty<string>());

            return GetSetting(key, new[] { envVarName });
        }

        /// <summary>
        /// Obtiene un valor de AppSettings, priorizando la primera variable de entorno disponible.
        /// </summary>
        /// <param name="key">Clave de AppSettings.</param>
        /// <param name="envVarNames">Variables de entorno candidatas en orden de prioridad.</param>
        /// <returns>Valor resuelto o null si no existe.</returns>
        public static string GetSetting(string key, params string[] envVarNames)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            string value = null;

            if (envVarNames != null)
            {
                foreach (var envVarName in envVarNames)
                {
                    if (string.IsNullOrWhiteSpace(envVarName))
                        continue;

                    value = Environment.GetEnvironmentVariable(envVarName);
                    if (!string.IsNullOrWhiteSpace(value))
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(value))
                return GetConfigSetting(key, envVarNames);

            return NormalizeExpandedValue(value, envVarNames);
        }

        /// <summary>
        /// Obtiene un valor solo desde AppSettings, expandiendo variables de entorno y anulando placeholders sin resolver.
        /// </summary>
        /// <param name="key">Clave de AppSettings.</param>
        /// <param name="envVarNames">Variables de entorno que pueden aparecer como placeholder en el valor.</param>
        /// <returns>Valor resuelto o null si no existe o sigue sin resolver.</returns>
        public static string GetConfigSetting(string key, params string[] envVarNames)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            var value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return NormalizeExpandedValue(value, envVarNames);
        }

        private static string NormalizeExpandedValue(string value, string[] envVarNames)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var expanded = Environment.ExpandEnvironmentVariables(value).Trim();
            if (string.IsNullOrWhiteSpace(expanded))
                return null;

            if (envVarNames != null)
            {
                foreach (var envVarName in envVarNames)
                {
                    if (string.IsNullOrWhiteSpace(envVarName))
                        continue;

                    var token = "%" + envVarName + "%";
                    if (string.Equals(expanded, token, StringComparison.OrdinalIgnoreCase))
                        return null;
                }
            }

            return expanded;
        }

        /// <summary>
        /// Obtiene un booleano desde variables de entorno o AppSettings.
        /// </summary>
        public static bool GetBoolSetting(string key, bool defaultValue = false, params string[] envVarNames)
        {
            var raw = GetSetting(key, envVarNames);
            return bool.TryParse(raw, out var parsed) ? parsed : defaultValue;
        }

        /// <summary>
        /// Obtiene un entero desde variables de entorno o AppSettings.
        /// </summary>
        public static int GetIntSetting(string key, int defaultValue, params string[] envVarNames)
        {
            var raw = GetSetting(key, envVarNames);
            return int.TryParse(raw, out var parsed) ? parsed : defaultValue;
        }
    }
}
