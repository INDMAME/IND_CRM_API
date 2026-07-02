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
        private const int BusinessConnectorSystemChangedHResult = unchecked((int)0x80041004);
        private static readonly SemaphoreSlim ComAccessSemaphore = new SemaphoreSlim(1, 1);
        private readonly IAxLogger _logger;
        private readonly string _configPath;
        private readonly string _defaultUser;
        private readonly string _defaultPass;
        private readonly bool _allowDefaultCredentials;
        private readonly int _callTimeoutSeconds;
        private readonly AxaptaComOptions _options;
        private readonly ComPlusApplicationController _comPlusController;
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
            _options = AxaptaComOptions.FromConfiguration();
            _comPlusController = new ComPlusApplicationController(_logger);
        }

        /// <summary>Current Business Connector safety options.</summary>
        public AxaptaComOptions Options => _options;

        /// <summary>
        /// Acquires the process-wide Business Connector gate when serialization is enabled.
        /// </summary>
        public IDisposable EnterComAccess(IND_AxRequestContext ctx, string reason)
        {
            if (!_options.SerializeComAccess)
                return NoopDisposable.Instance;

            Log("com-gate", ctx, AxaptaSessionManager.LogLevel.Info, reason, stage: "wait");
            ComAccessSemaphore.Wait();
            Log("com-gate", ctx, AxaptaSessionManager.LogLevel.Info, reason, stage: "acquired");
            return new ComAccessLease(this, ctx, reason);
        }

        private void ReleaseComAccess(IND_AxRequestContext ctx, string reason)
        {
            ComAccessSemaphore.Release();
            Log("com-gate", ctx, AxaptaSessionManager.LogLevel.Info, reason, stage: "released");
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
                            SafeLogoffAndDispose(innerAx, ctx, "logon-failed");
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
                SafeLogoffAndDispose(ax, ctx, "logon-failed");
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
            IDisposable comAccessLease = null;
            var sw = Stopwatch.StartNew();
            try
            {
                comAccessLease = EnterComAccess(ctx, "smoke-test");
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

                comAccessLease?.Dispose();

                if (createdContext)
                    IND_AxRequestContext.Clear();
            }
        }

        // Executes an action and retries once on session errors.
        public T ExecuteWithRetryOnSessionErrors<T>(
            Func<T> action,
            Func<T> retryAction,
            IND_AxRequestContext ctx,
            string actionName,
            Func<Exception, bool> recoverBeforeRetry = null)
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
                var isSystemChanged = IsBusinessConnectorSystemChanged(ex);
                if (!isSystemChanged && !IsSessionError(ex))
                {
                    Log("call", ctx, AxaptaSessionManager.LogLevel.Error, actionName, stage: "non-session-exception", ex: ex);
                    throw;
                }

                if (isSystemChanged)
                {
                    Log("call", ctx, AxaptaSessionManager.LogLevel.Error, actionName, stage: "business-connector-system-changed", ex: ex);
                    var recovered = recoverBeforeRetry != null && recoverBeforeRetry(ex);
                    if (!recovered)
                    {
                        Log("call", ctx, AxaptaSessionManager.LogLevel.Error, actionName, stage: "recovery-not-available", ex: ex);
                        throw;
                    }
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
            {
                LogComFailure(error, ctx, operationName, detail);
                ExceptionDispatchInfo.Capture(error).Throw();
            }

            return result;
        }

        /// <summary>
        /// Releases tracked AX objects, logs off, and releases the owned Axapta COM session.
        /// </summary>
        public void SafeLogoffAndDispose(AxaptaComSession session, IND_AxRequestContext ctx, string reason)
        {
            if (session == null)
                return;

            try
            {
                session.ReleaseTrackedObjects(reason);
            }
            catch (Exception ex)
            {
                Log("release", ctx, AxaptaSessionManager.LogLevel.Warning, reason, stage: "tracked-release-exception", ex: ex);
            }

            var ax = session.DetachRawAxapta();
            SafeLogoffAndDispose(ax, ctx, reason);
        }

        /// <summary>
        /// Logs off and releases an owned raw Axapta COM instance.
        /// </summary>
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
                SafeDisposeObject(ax, ctx, reason, "axapta-session");
                try
                {
                    var releaseCalls = SafeReleaseCom(ax);
                    Log("logoff", ctx, AxaptaSessionManager.LogLevel.Info, reason, stage: "after-release-com", detail: "releaseCalls=" + releaseCalls, durationMs: (int)sw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    Log("logoff", ctx, AxaptaSessionManager.LogLevel.Warning, reason, stage: "release-com-warning", ex: ex);
                }
            }
        }

        /// <summary>
        /// Disposes and releases an owned AX/COM object without hiding the original functional error.
        /// </summary>
        public int SafeReleaseAxObject(object axObject, IND_AxRequestContext ctx, string reason, string objectName)
        {
            if (axObject == null)
                return 0;

            SafeDisposeObject(axObject, ctx, reason, objectName);

            try
            {
                if (!Marshal.IsComObject(axObject))
                    return 0;

                var releaseCalls = Marshal.FinalReleaseComObject(axObject);
                Log("release", ctx, AxaptaSessionManager.LogLevel.Info, reason, stage: "released-owned-object", detail: objectName + " releaseCalls=" + releaseCalls);
                return releaseCalls;
            }
            catch (Exception ex)
            {
                Log("release", ctx, AxaptaSessionManager.LogLevel.Warning, reason, stage: "release-owned-object-warning", detail: objectName, ex: ex);
                return 0;
            }
        }

        /// <summary>
        /// Logs that an AX/COM object is now owned by the current session scope.
        /// </summary>
        public void LogTrackedAxObject(IND_AxRequestContext ctx, string reason, object axObject)
        {
            var typeName = axObject == null ? "null" : axObject.GetType().FullName;
            Log("track", ctx, AxaptaSessionManager.LogLevel.Info, reason, stage: "owned-object", detail: "type=" + typeName);
        }

        /// <summary>
        /// Restarts COM+ for a Business Connector system-change failure when recovery is enabled.
        /// </summary>
        public bool TryRecoverBusinessConnector(IND_AxRequestContext ctx, Exception ex, string operationName, string detail)
        {
            if (!_options.RestartComPlusOnSystemChanged)
                return false;

            var hresult = GetHResult(ex);
            Log(
                "recovery",
                ctx,
                AxaptaSessionManager.LogLevel.Warning,
                operationName,
                stage: "restart-complus-begin",
                detail: "hresult=" + FormatHResult(hresult) + " app=" + _options.ComPlusApplicationName + " " + (detail ?? string.Empty),
                ex: ex);

            return _comPlusController.RestartApplication(_options.ComPlusApplicationName, ctx, hresult, operationName);
        }

        /// <summary>
        /// Shuts down COM+ after a call only when the explicit maintenance switch is enabled.
        /// </summary>
        public void TryShutdownComPlusAfterCall(IND_AxRequestContext ctx, string reason)
        {
            if (!_options.ShutdownComPlusAfterCall)
                return;

            _comPlusController.ShutdownApplication(_options.ComPlusApplicationName, ctx, reason);
        }

        /// <summary>
        /// Detects the Business Connector connected-to-another-system failure pattern.
        /// </summary>
        public bool IsBusinessConnectorSystemChanged(Exception ex)
        {
            if (ex == null)
                return false;

            if (ex is COMException comEx && comEx.ErrorCode == BusinessConnectorSystemChangedHResult)
                return true;

            var typeName = ex.GetType().FullName ?? string.Empty;
            if (typeName.IndexOf("LogonSystemChangedException", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            var msg = (ex.Message ?? string.Empty).ToLowerInvariant();
            return msg.Contains("connected to another system") ||
                   msg.Contains("conectado a otro sistema") ||
                   msg.Contains("business connector ya conectado");
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

        private void SafeDisposeObject(object obj, IND_AxRequestContext ctx, string reason, string objectName)
        {
            try
            {
                var disposable = obj as IDisposable;
                disposable?.Dispose();
            }
            catch (Exception ex)
            {
                Log("release", ctx, AxaptaSessionManager.LogLevel.Warning, reason, stage: "dispose-warning", detail: objectName, ex: ex);
            }
        }

        private void LogComFailure(Exception error, IND_AxRequestContext ctx, string operationName, string detail)
        {
            if (error == null)
                return;

            if (IsBusinessConnectorSystemChanged(error))
            {
                Log(
                    "call",
                    ctx,
                    AxaptaSessionManager.LogLevel.Error,
                    operationName,
                    stage: "business-connector-system-changed",
                    detail: "hresult=" + FormatHResult(GetHResult(error)) + " possibleProcessContamination=true " + (detail ?? string.Empty),
                    ex: error);
                return;
            }

            if (error is COMException)
            {
                Log(
                    "call",
                    ctx,
                    AxaptaSessionManager.LogLevel.Error,
                    operationName,
                    stage: "com-exception",
                    detail: "hresult=" + FormatHResult(GetHResult(error)) + " " + (detail ?? string.Empty),
                    ex: error);
            }
        }

        private static int GetHResult(Exception ex)
        {
            var comEx = ex as COMException;
            return comEx?.ErrorCode ?? ex?.HResult ?? 0;
        }

        private static string FormatHResult(int hresult)
        {
            return "0x" + unchecked((uint)hresult).ToString("X8");
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

        private sealed class ComAccessLease : IDisposable
        {
            private readonly IND_AxSessionGuard _owner;
            private readonly IND_AxRequestContext _ctx;
            private readonly string _reason;
            private bool _disposed;

            public ComAccessLease(IND_AxSessionGuard owner, IND_AxRequestContext ctx, string reason)
            {
                _owner = owner;
                _ctx = ctx;
                _reason = reason;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _owner.ReleaseComAccess(_ctx, _reason);
            }
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new NoopDisposable();

            private NoopDisposable()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
