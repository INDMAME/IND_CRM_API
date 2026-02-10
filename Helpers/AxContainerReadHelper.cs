using AxaptaCOMConnector;
using System;

namespace IND_CRM_API.Helpers
{
    /// <summary>
    /// Lectura defensiva de contenedores AX para evitar excepciones COM en controladores.
    /// </summary>
    public static class AxContainerReadHelper
    {
        /// <summary>
        /// Obtiene el largo del contenedor de forma segura.
        /// </summary>
        public static int SafeLength(IAxaptaContainer container)
        {
            try
            {
                return container?.Length() ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Lee un sub-contenedor por indice sin romper el flujo.
        /// </summary>
        public static IAxaptaContainer SafePeekContainer(IAxaptaContainer container, int index)
        {
            try
            {
                return container?.Peek(index) as IAxaptaContainer;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Lee un valor crudo por indice sin lanzar excepciones.
        /// </summary>
        public static object SafeValue(IAxaptaContainer container, int index)
        {
            try
            {
                return container?.Peek(index);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Convierte un valor del contenedor a string de forma segura.
        /// </summary>
        public static string SafeString(IAxaptaContainer container, int index)
        {
            try
            {
                return container?.Peek(index)?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Detecta el marcador comun de AX para lista sin resultados.
        /// </summary>
        public static bool IsSinDatos(IAxaptaContainer root, out string message)
        {
            message = string.Empty;
            if (root == null || SafeLength(root) == 0)
                return false;

            if (SafeLength(root) != 1)
                return false;

            var single = SafeValue(root, 1);
            if (single is string str && str.Equals("Sin datos.", StringComparison.OrdinalIgnoreCase))
            {
                message = "Sin datos.";
                return true;
            }

            var row = single as IAxaptaContainer;
            if (row != null && SafeLength(row) == 1)
            {
                var first = SafeString(row, 1);
                if (first.Equals("Sin datos.", StringComparison.OrdinalIgnoreCase))
                {
                    message = "Sin datos.";
                    return true;
                }
            }

            return false;
        }
    }
}
