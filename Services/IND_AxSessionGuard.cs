using AxaptaCOMConnector;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Centralizes Axapta session lifecycle: logon, retry, and safe disposal.
    /// </summary>
    public sealed class IND_AxSessionGuard
    {
        private readonly IAxLogger _logger;
        private readonly string _configPath;
        private readonly string _defaultUser;
        private readonly string _defaultPass;
        private readonly bool _allowDefaultCredentials;

        public IND_AxSessionGuard(IAxLogger logger, string configPath, string defaultUser, string defaultPass, bool allowDefaultCredentials)
        {
            _logger = logger ?? new FileAxLogger();
            _configPath = configPath;
            _defaultUser = defaultUser;
            _defaultPass = defaultPass;
            _allowDefaultCredentials = allowDefaultCredentials;
        }

        // Logs on to Axapta and returns a live COM instance.
        public Axapta2Class EnsureLoggedOn(string username, string password, IND_AxRequestContext ctx)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new Exception("Usuario no valido.");

            if (string.IsNullOrWhiteSpace(password))
                throw new Exception("Password de Axapta vacio.");

            var sw = Stopwatch.StartNew();
            var ax = new Axapta2Class();
            try
            {
                ax.Logon2(username, password, "", "", "", "", _configPath, false, null, null);
                sw.Stop();
                Log("logon", ctx, AxaptaSessionManager.LogLevel.Info, null, durationMs: (int)sw.ElapsedMilliseconds);
                return ax;
            }
            catch (Exception ex)
            {
                sw.Stop();
                SafeReleaseCom(ax);
                Log("logon", ctx, AxaptaSessionManager.LogLevel.Error, "logon-failed", durationMs: (int)sw.ElapsedMilliseconds, ex: ex);
                throw;
            }
        }

        // Smoke test: logon + simple call + logoff.
        public bool SmokeTest(string username, string password, IND_AxRequestContext ctx)
        {
            var createdContext = false;
            if (ctx == null)
            {
                IND_AxRequestContext.Start(Guid.NewGuid().ToString("N"), "smoke", string.Empty);
                ctx = IND_AxRequestContext.Current;
                createdContext = true;
            }

            Axapta2Class ax = null;
            var sw = Stopwatch.StartNew();
            try
            {
                ax = EnsureLoggedOn(username, password, ctx);
                // Llamada inocua para validar conexion.
                ax.CallStaticClassMethod("INDCRMApiClass", "getEnvironmentName");
                sw.Stop();
                Log("smoke-test", ctx, AxaptaSessionManager.LogLevel.Info, null, durationMs: (int)sw.ElapsedMilliseconds);
                return true;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Log("smoke-test", ctx, AxaptaSessionManager.LogLevel.Warning, "smoke-failed", durationMs: (int)sw.ElapsedMilliseconds, ex: ex);
                return false;
            }
            finally
            {
                if (ax != null)
                    SafeLogoffAndDispose(ax, ctx, "smoke-test");

                if (createdContext)
                    IND_AxRequestContext.Clear();
            }
        }

        // Executes an action and retries once on session errors.
        public T ExecuteWithRetryOnSessionErrors<T>(Func<T> action, Func<T> retryAction, IND_AxRequestContext ctx, string actionName)
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                if (!IsSessionError(ex))
                {
                    Log("call", ctx, AxaptaSessionManager.LogLevel.Error, actionName, ex: ex);
                    throw;
                }

                Log("call", ctx, AxaptaSessionManager.LogLevel.Warning, "session-error", retries: 1, ex: ex);
                return retryAction();
            }
        }

        // Safe logoff and COM release.
        public void SafeLogoffAndDispose(Axapta2Class ax, IND_AxRequestContext ctx, string reason)
        {
            if (ax == null)
                return;

            try
            {
                ax.Logoff();
            }
            catch (Exception ex)
            {
                Log("logoff", ctx, AxaptaSessionManager.LogLevel.Warning, reason, ex: ex);
            }
            finally
            {
                SafeReleaseCom(ax);
                Log("logoff", ctx, AxaptaSessionManager.LogLevel.Info, reason);
            }
        }

        // Heuristic to detect session-related failures.
        private bool IsSessionError(Exception ex)
        {
            if (ex == null)
                return false;

            if (ex is COMException)
                return true;

            var msg = (ex.Message ?? string.Empty).ToLowerInvariant();
            return msg.Contains("session") ||
                   msg.Contains("logon") ||
                   msg.Contains("login") ||
                   msg.Contains("not logged") ||
                   msg.Contains("invalid") ||
                   msg.Contains("expired");
        }

        // Structured log helper with correlation data.
        private void Log(string action, IND_AxRequestContext ctx, AxaptaSessionManager.LogLevel level, string reason,
            int? durationMs = null, int? retries = null, Exception ex = null)
        {
            var parts = new List<string>
            {
                "[AX-SESSION]",
                "action=" + (action ?? string.Empty),
                "correlationId=" + (ctx?.CorrelationId ?? "-"),
                "axUser=" + (ctx?.Username ?? "-"),
                "company=" + (ctx?.Company ?? "-"),
                "endpoint=" + (ctx?.Endpoint ?? "-")
            };

            if (!string.IsNullOrWhiteSpace(reason))
                parts.Add("reason=" + reason);
            if (durationMs.HasValue)
                parts.Add("durationMs=" + durationMs.Value);
            if (retries.HasValue)
                parts.Add("retries=" + retries.Value);
            if (ex != null)
            {
                parts.Add("error=" + ex.GetType().Name);
                parts.Add("message=" + (ex.Message ?? string.Empty));
                if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                    parts.Add("callStackText=" + Truncate(ex.StackTrace, 4000));
            }

            _logger.Log(string.Join(" ", parts), level);
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value;
            return value.Substring(0, maxLength);
        }

        // Releases COM references until fully released.
        private static void SafeReleaseCom(object obj)
        {
            if (obj != null && Marshal.IsComObject(obj))
            {
                while (Marshal.ReleaseComObject(obj) > 0) { }
            }
        }
    }
}
