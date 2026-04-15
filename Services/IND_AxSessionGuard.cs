using AxaptaCOMConnector;
using IND_CRM_API.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Centralizes Axapta session lifecycle: logon, retry, and safe disposal.
    /// </summary>
    public sealed class IND_AxSessionGuard
    {
        private const int DefaultCallTimeoutSeconds = 90;
        private readonly IAxLogger _logger;
        private readonly string _configPath;
        private readonly string _defaultUser;
        private readonly string _defaultPass;
        private readonly bool _allowDefaultCredentials;
        private readonly int _callTimeoutSeconds;
        private readonly object _timeoutStateLock = new object();
        private DateTime _axUnavailableUntilUtc = DateTime.MinValue;

        public IND_AxSessionGuard(IAxLogger logger, string configPath, string defaultUser, string defaultPass, bool allowDefaultCredentials)
        {
            _logger = logger ?? new FileAxLogger();
            _configPath = configPath;
            _defaultUser = defaultUser;
            _defaultPass = defaultPass;
            _allowDefaultCredentials = allowDefaultCredentials;
            _callTimeoutSeconds = ReadCallTimeoutSeconds();
        }

        // Logs on to Axapta and returns a live COM instance.
        public Axapta2Class EnsureLoggedOn(string username, string password, IND_AxRequestContext ctx)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new Exception("Usuario no valido.");

            if (string.IsNullOrWhiteSpace(password))
                throw new Exception("Password de Axapta vacio.");

            var sw = Stopwatch.StartNew();
            Log("logon", ctx, AxaptaSessionManager.LogLevel.Info, null, stage: "begin", detail: "configPath=" + (_configPath ?? string.Empty));

            Axapta2Class ax = null;
            try
            {
                ax = ExecuteComCall(
                    () =>
                    {
                        Axapta2Class innerAx = null;
                        try
                        {
                            innerAx = new Axapta2Class();
                            Log("logon", ctx, AxaptaSessionManager.LogLevel.Info, null, stage: "com-instance-created", detail: "instanceType=" + innerAx.GetType().FullName, durationMs: (int)sw.ElapsedMilliseconds);

                            Log("logon", ctx, AxaptaSessionManager.LogLevel.Info, null, stage: "before-logon2", detail: "username=" + username, durationMs: (int)sw.ElapsedMilliseconds);
                            innerAx.Logon2(username, password, "", "", "", "", _configPath, false, null, null);
                            Log("logon", ctx, AxaptaSessionManager.LogLevel.Info, null, stage: "after-logon2", durationMs: (int)sw.ElapsedMilliseconds);
                            return innerAx;
                        }
                        catch
                        {
                            SafeReleaseCom(innerAx);
                            throw;
                        }
                    },
                    ctx,
                    "logon2",
                    "username=" + username);

                sw.Stop();
                Log("logon", ctx, AxaptaSessionManager.LogLevel.Info, null, stage: "success", durationMs: (int)sw.ElapsedMilliseconds);
                return ax;
            }
            catch (Exception ex)
            {
                sw.Stop();
                SafeReleaseCom(ax);
                Log("logon", ctx, AxaptaSessionManager.LogLevel.Error, "logon-failed", stage: "exception", durationMs: (int)sw.ElapsedMilliseconds, ex: ex);
                throw;
            }
        }

        // Smoke test: logon + simple call + logoff.
        public bool SmokeTest(string username, string password, IND_AxRequestContext ctx)
        {
            var createdContext = false;
            if (ctx == null)
            {
                var correlationId = Guid.NewGuid().ToString("N");
                IND_AxRequestContext.Start(correlationId, Guid.NewGuid().ToString("N"), "smoke", string.Empty);
                ctx = IND_AxRequestContext.Current;
                createdContext = true;
            }

            Axapta2Class ax = null;
            var sw = Stopwatch.StartNew();
            try
            {
                Log("smoke-test", ctx, AxaptaSessionManager.LogLevel.Info, null, stage: "begin", detail: "createdContext=" + createdContext);
                Log("smoke-test", ctx, AxaptaSessionManager.LogLevel.Info, null, stage: "before-ensure-logged-on", detail: "username=" + username, durationMs: (int)sw.ElapsedMilliseconds);
                ax = EnsureLoggedOn(username, password, ctx);
                Log("smoke-test", ctx, AxaptaSessionManager.LogLevel.Info, null, stage: "after-ensure-logged-on", durationMs: (int)sw.ElapsedMilliseconds);

                // Llamada inocua para validar conexion.
                Log("smoke-test", ctx, AxaptaSessionManager.LogLevel.Info, null, stage: "before-get-environment-name", durationMs: (int)sw.ElapsedMilliseconds);
                ExecuteComCall(
                    () =>
                    {
                        ax.CallStaticClassMethod("INDCRMUtilityService", "getEnvironmentName");
                        return true;
                    },
                    ctx,
                    "smoke-getEnvironmentName",
                    "class=INDCRMUtilityService method=getEnvironmentName");
                Log("smoke-test", ctx, AxaptaSessionManager.LogLevel.Info, null, stage: "after-get-environment-name", durationMs: (int)sw.ElapsedMilliseconds);
                sw.Stop();
                Log("smoke-test", ctx, AxaptaSessionManager.LogLevel.Info, null, stage: "success", durationMs: (int)sw.ElapsedMilliseconds);
                return true;
            }
            catch (IND_AxCallTimeoutException)
            {
                sw.Stop();
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Log("smoke-test", ctx, AxaptaSessionManager.LogLevel.Warning, "smoke-failed", stage: "exception", durationMs: (int)sw.ElapsedMilliseconds, ex: ex);
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
                Log("call", ctx, AxaptaSessionManager.LogLevel.Info, actionName, stage: "primary-attempt");
                var result = action();
                Log("call", ctx, AxaptaSessionManager.LogLevel.Info, actionName, stage: "primary-success");
                return result;
            }
            catch (Exception ex)
            {
                if (!IsSessionError(ex))
                {
                    Log("call", ctx, AxaptaSessionManager.LogLevel.Error, actionName, stage: "non-session-exception", ex: ex);
                    throw;
                }

                Log("call", ctx, AxaptaSessionManager.LogLevel.Warning, "session-error", stage: "primary-failed-retrying", retries: 1, ex: ex);
                Log("call", ctx, AxaptaSessionManager.LogLevel.Info, actionName, stage: "retry-attempt");
                var retryResult = retryAction();
                Log("call", ctx, AxaptaSessionManager.LogLevel.Info, actionName, stage: "retry-success", retries: 1);
                return retryResult;
            }
        }

        // Executes one COM call with fail-fast timeout detection and a short cooldown after hangs.
        public T ExecuteComCall<T>(Func<T> action, IND_AxRequestContext ctx, string operationName, string detail)
        {
            EnsureAxAvailability(ctx, operationName, detail);

            T result = default(T);
            Exception error = null;
            using (var done = new ManualResetEventSlim(false))
            {
                var worker = new Thread(() =>
                {
                    try
                    {
                        result = action();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                });

                worker.IsBackground = true;
                TryCopyApartmentState(worker);
                worker.Start();

                if (!done.Wait(TimeSpan.FromSeconds(_callTimeoutSeconds)))
                {
                    RegisterTimeout(ctx, operationName, detail);
                    throw new IND_AxCallTimeoutException(operationName, _callTimeoutSeconds, detail);
                }
            }

            if (error != null)
                ExceptionDispatchInfo.Capture(error).Throw();

            return result;
        }

        // Safe logoff and COM release.
        public void SafeLogoffAndDispose(Axapta2Class ax, IND_AxRequestContext ctx, string reason)
        {
            if (ax == null)
                return;

            var sw = Stopwatch.StartNew();
            try
            {
                Log("logoff", ctx, AxaptaSessionManager.LogLevel.Info, reason, stage: "before-logoff");
                ax.Logoff();
                Log("logoff", ctx, AxaptaSessionManager.LogLevel.Info, reason, stage: "after-logoff", durationMs: (int)sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                Log("logoff", ctx, AxaptaSessionManager.LogLevel.Warning, reason, stage: "logoff-exception", ex: ex);
            }
            finally
            {
                if (sw.IsRunning)
                    sw.Stop();

                Log("logoff", ctx, AxaptaSessionManager.LogLevel.Info, reason, stage: "before-release-com", durationMs: (int)sw.ElapsedMilliseconds);
                var releaseCalls = SafeReleaseCom(ax);
                Log("logoff", ctx, AxaptaSessionManager.LogLevel.Info, reason, stage: "after-release-com", detail: "releaseCalls=" + releaseCalls, durationMs: (int)sw.ElapsedMilliseconds);
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
            string stage = null, string detail = null, int? durationMs = null, int? retries = null, Exception ex = null)
        {
            var ctxAgeMs = ctx == null ? 0 : (int)Math.Max(0, (DateTime.UtcNow - ctx.StartedUtc).TotalMilliseconds);
            var parts = new List<string>
            {
                "[AX-SESSION]",
                "action=" + (action ?? string.Empty),
                "stage=" + (stage ?? string.Empty),
                "correlationId=" + (ctx?.CorrelationId ?? "-"),
                "traceId=" + (ctx?.TraceId ?? "-"),
                "axUser=" + (ctx?.Username ?? "-"),
                "company=" + (ctx?.Company ?? "-"),
                "endpoint=" + (ctx?.Endpoint ?? "-"),
                "threadId=" + Environment.CurrentManagedThreadId,
                "apartment=" + Thread.CurrentThread.GetApartmentState(),
                "processId=" + Process.GetCurrentProcess().Id,
                "ctxAgeMs=" + ctxAgeMs
            };

            if (!string.IsNullOrWhiteSpace(reason))
                parts.Add("reason=" + reason);
            if (!string.IsNullOrWhiteSpace(detail))
                parts.Add("detail=" + Truncate(detail, 3000));
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
        private static int SafeReleaseCom(object obj)
        {
            var releaseCalls = 0;
            if (obj != null && Marshal.IsComObject(obj))
            {
                while (Marshal.ReleaseComObject(obj) > 0)
                {
                    releaseCalls++;
                }

                releaseCalls++;
            }

            return releaseCalls;
        }

        private static int ReadCallTimeoutSeconds()
        {
            var raw = AppSettingsHelper.GetSetting("Axapta.CallTimeoutSeconds", "AXAPTA_CALL_TIMEOUT_SECONDS");
            return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : DefaultCallTimeoutSeconds;
        }

        private void EnsureAxAvailability(IND_AxRequestContext ctx, string operationName, string detail)
        {
            DateTime unavailableUntilUtc;
            lock (_timeoutStateLock)
            {
                unavailableUntilUtc = _axUnavailableUntilUtc;
            }

            if (unavailableUntilUtc <= DateTime.UtcNow)
                return;

            Log(
                "timeout",
                ctx,
                AxaptaSessionManager.LogLevel.Warning,
                operationName,
                stage: "cooldown-active",
                detail: "untilUtc=" + unavailableUntilUtc.ToString("o") + " " + (detail ?? string.Empty));
            throw new IND_AxCallTimeoutException(operationName, _callTimeoutSeconds, detail);
        }

        private void RegisterTimeout(IND_AxRequestContext ctx, string operationName, string detail)
        {
            var cooldownSeconds = Math.Min(Math.Max(_callTimeoutSeconds, 10), 60);
            var untilUtc = DateTime.UtcNow.AddSeconds(cooldownSeconds);
            lock (_timeoutStateLock)
            {
                _axUnavailableUntilUtc = untilUtc;
            }

            Log(
                "timeout",
                ctx,
                AxaptaSessionManager.LogLevel.Error,
                operationName,
                stage: "timeout-open-circuit",
                detail: "timeoutSeconds=" + _callTimeoutSeconds + " cooldownUntilUtc=" + untilUtc.ToString("o") + " " + (detail ?? string.Empty));
        }

        private static void TryCopyApartmentState(Thread worker)
        {
            try
            {
                var apartmentState = Thread.CurrentThread.GetApartmentState();
                if (apartmentState == ApartmentState.STA || apartmentState == ApartmentState.MTA)
                    worker.SetApartmentState(apartmentState);
            }
            catch
            {
                // Best effort only. Timeout protection still works without forcing the apartment state.
            }
        }
    }
}
