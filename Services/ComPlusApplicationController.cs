using System;
using System.Runtime.InteropServices;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Controls COM+ application recovery through COMAdmin when explicitly enabled.
    /// </summary>
    public sealed class ComPlusApplicationController
    {
        private static readonly object RecoveryLock = new object();
        private readonly IAxLogger _logger;

        /// <summary>
        /// Creates a COM+ controller that logs all recovery actions through the project logger.
        /// </summary>
        public ComPlusApplicationController(IAxLogger logger)
        {
            _logger = logger ?? new FileAxLogger();
        }

        /// <summary>
        /// Restarts the configured COM+ application as an explicit recovery action.
        /// </summary>
        public bool RestartApplication(string applicationName, IND_AxRequestContext ctx, int hresult, string reason)
        {
            if (string.IsNullOrWhiteSpace(applicationName))
            {
                Log(ctx, "restart-skipped", applicationName, hresult, reason, "missing application name", AxaptaSessionManager.LogLevel.Warning);
                return false;
            }

            lock (RecoveryLock)
            {
                object catalog = null;
                try
                {
                    var catalogType = Type.GetTypeFromProgID("COMAdmin.COMAdminCatalog");
                    if (catalogType == null)
                    {
                        Log(ctx, "restart-failed", applicationName, hresult, reason, "COMAdmin.COMAdminCatalog not available", AxaptaSessionManager.LogLevel.Warning);
                        return false;
                    }

                    catalog = Activator.CreateInstance(catalogType);
                    dynamic dynamicCatalog = catalog;

                    Log(ctx, "shutdown-begin", applicationName, hresult, reason, null, AxaptaSessionManager.LogLevel.Warning);
                    dynamicCatalog.ShutdownApplication(applicationName);
                    Log(ctx, "start-begin", applicationName, hresult, reason, null, AxaptaSessionManager.LogLevel.Warning);
                    dynamicCatalog.StartApplication(applicationName);
                    Log(ctx, "restart-success", applicationName, hresult, reason, null, AxaptaSessionManager.LogLevel.Warning);
                    return true;
                }
                catch (Exception ex)
                {
                    Log(ctx, "restart-failed", applicationName, hresult, reason, ex.Message, AxaptaSessionManager.LogLevel.Error);
                    return false;
                }
                finally
                {
                    TryReleaseCatalog(catalog, ctx, applicationName);
                }
            }
        }

        /// <summary>
        /// Shuts down the configured COM+ application as a controlled maintenance action.
        /// </summary>
        public bool ShutdownApplication(string applicationName, IND_AxRequestContext ctx, string reason)
        {
            if (string.IsNullOrWhiteSpace(applicationName))
                return false;

            lock (RecoveryLock)
            {
                object catalog = null;
                try
                {
                    var catalogType = Type.GetTypeFromProgID("COMAdmin.COMAdminCatalog");
                    if (catalogType == null)
                    {
                        Log(ctx, "shutdown-failed", applicationName, 0, reason, "COMAdmin.COMAdminCatalog not available", AxaptaSessionManager.LogLevel.Warning);
                        return false;
                    }

                    catalog = Activator.CreateInstance(catalogType);
                    dynamic dynamicCatalog = catalog;
                    Log(ctx, "shutdown-begin", applicationName, 0, reason, null, AxaptaSessionManager.LogLevel.Warning);
                    dynamicCatalog.ShutdownApplication(applicationName);
                    Log(ctx, "shutdown-success", applicationName, 0, reason, null, AxaptaSessionManager.LogLevel.Warning);
                    return true;
                }
                catch (Exception ex)
                {
                    Log(ctx, "shutdown-failed", applicationName, 0, reason, ex.Message, AxaptaSessionManager.LogLevel.Error);
                    return false;
                }
                finally
                {
                    TryReleaseCatalog(catalog, ctx, applicationName);
                }
            }
        }

        private void TryReleaseCatalog(object catalog, IND_AxRequestContext ctx, string applicationName)
        {
            if (catalog == null)
                return;

            try
            {
                if (Marshal.IsComObject(catalog))
                    Marshal.FinalReleaseComObject(catalog);
            }
            catch (Exception ex)
            {
                Log(ctx, "release-catalog-warning", applicationName, 0, "COMAdmin release", ex.Message, AxaptaSessionManager.LogLevel.Warning);
            }
        }

        private void Log(IND_AxRequestContext ctx, string stage, string applicationName, int hresult, string reason, string detail, AxaptaSessionManager.LogLevel level)
        {
            _logger.Log(
                "[AX-COMPLUS] " +
                "stage=" + (stage ?? string.Empty) + " " +
                "traceId=" + (ctx?.TraceId ?? "-") + " " +
                "correlationId=" + (ctx?.CorrelationId ?? "-") + " " +
                "applicationName=" + (applicationName ?? string.Empty) + " " +
                "hresult=" + FormatHResult(hresult) + " " +
                "reason=" + (reason ?? string.Empty) + " " +
                "detail=" + (detail ?? string.Empty),
                level);
        }

        private static string FormatHResult(int hresult)
        {
            return "0x" + unchecked((uint)hresult).ToString("X8");
        }
    }
}
