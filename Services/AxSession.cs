using System;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Singleton compartido de AxaptaSessionManager.
    /// Se inicializa desde DependencyConfig para evitar multiples instancias.
    /// </summary>
    public static class AxSession
    {
        private static AxaptaSessionManager _manager;

        /// <summary>Instancia compartida (puede ser null si no se inicializo).</summary>
        public static AxaptaSessionManager Manager => _manager;

        /// <summary>
        /// Inicializa el singleton con la instancia compartida.
        /// </summary>
        public static void Initialize(AxaptaSessionManager manager)
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));

            if (_manager == null)
                _manager = manager;
        }
    }
}
