using IND_CRM_API.Helpers;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Reads Axapta Business Connector safety switches from application configuration.
    /// </summary>
    public sealed class AxaptaComOptions
    {
        /// <summary>
        /// Creates immutable Business Connector safety options.
        /// </summary>
        public AxaptaComOptions(
            bool serializeComAccess,
            bool shutdownComPlusAfterCall,
            bool restartComPlusOnSystemChanged,
            string comPlusApplicationName)
        {
            SerializeComAccess = serializeComAccess;
            ShutdownComPlusAfterCall = shutdownComPlusAfterCall;
            RestartComPlusOnSystemChanged = restartComPlusOnSystemChanged;
            ComPlusApplicationName = string.IsNullOrWhiteSpace(comPlusApplicationName)
                ? "Navision Axapta Business Connector"
                : comPlusApplicationName.Trim();
        }

        /// <summary>Serializes all Axapta COM access inside this API process.</summary>
        public bool SerializeComAccess { get; }

        /// <summary>Allows controlled COM+ shutdown after a completed Axapta request.</summary>
        public bool ShutdownComPlusAfterCall { get; }

        /// <summary>Allows controlled COM+ restart after Business Connector system-change failures.</summary>
        public bool RestartComPlusOnSystemChanged { get; }

        /// <summary>Name of the COM+ application that hosts Axapta Business Connector.</summary>
        public string ComPlusApplicationName { get; }

        /// <summary>
        /// Loads Business Connector safety options from app settings and machine environment variables.
        /// </summary>
        public static AxaptaComOptions FromConfiguration()
        {
            return new AxaptaComOptions(
                AppSettingsHelper.GetBoolSetting("Axapta:SerializeComAccess", true, "AXAPTA_SERIALIZE_COM_ACCESS"),
                AppSettingsHelper.GetBoolSetting("Axapta:ShutdownComPlusAfterCall", false, "AXAPTA_SHUTDOWN_COMPLUS_AFTER_CALL"),
                AppSettingsHelper.GetBoolSetting("Axapta:RestartComPlusOnSystemChanged", false, "AXAPTA_RESTART_COMPLUS_ON_SYSTEM_CHANGED"),
                AppSettingsHelper.GetSetting("Axapta:ComPlusApplicationName", "AXAPTA_COMPLUS_APPLICATION_NAME"));
        }
    }
}
