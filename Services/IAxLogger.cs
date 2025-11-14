using System;

namespace IND_CRM_API.Services
{
    // Interfaz simple para logging de Axapta
    public interface IAxLogger
    {
        void Log(string message, AxaptaSessionManager.LogLevel level = AxaptaSessionManager.LogLevel.Info);
    }
}
