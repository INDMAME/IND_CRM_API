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
            if (string.IsNullOrWhiteSpace(key))
                return null;

            string value = null;

            if (!string.IsNullOrWhiteSpace(envVarName))
                value = Environment.GetEnvironmentVariable(envVarName);

            if (string.IsNullOrWhiteSpace(value))
                value = ConfigurationManager.AppSettings[key];

            if (string.IsNullOrWhiteSpace(value))
                return null;

            var expanded = Environment.ExpandEnvironmentVariables(value).Trim();
            if (string.IsNullOrWhiteSpace(expanded))
                return null;

            if (!string.IsNullOrWhiteSpace(envVarName))
            {
                var token = "%" + envVarName + "%";
                if (string.Equals(expanded, token, StringComparison.OrdinalIgnoreCase))
                    return null;
            }

            return expanded;
        }
    }
}
