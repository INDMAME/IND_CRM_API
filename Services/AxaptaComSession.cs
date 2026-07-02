using AxaptaCOMConnector;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Wraps one owned Axapta COM session and tracks AX objects created during the request.
    /// </summary>
    public sealed class AxaptaComSession
    {
        private readonly IND_AxSessionGuard _guard;
        private readonly IND_AxRequestContext _ctx;
        private readonly List<object> _ownedObjects = new List<object>();
        private readonly object _ownedObjectsLock = new object();
        private Axapta2Class _axapta;
        private bool _disposed;

        public AxaptaComSession(
            Axapta2Class axapta,
            IND_AxSessionGuard guard,
            IND_AxRequestContext ctx,
            string username,
            string configPath,
            string company)
        {
            _axapta = axapta ?? throw new ArgumentNullException(nameof(axapta));
            _guard = guard ?? throw new ArgumentNullException(nameof(guard));
            _ctx = ctx;
            Username = username ?? string.Empty;
            ConfigPath = configPath ?? string.Empty;
            Company = company ?? string.Empty;
        }

        /// <summary>Axapta user that owns this logged-on COM session.</summary>
        public string Username { get; }

        /// <summary>AXC configuration path used for this logged-on COM session.</summary>
        public string ConfigPath { get; }

        /// <summary>Business company associated with the current request scope.</summary>
        public string Company { get; }

        internal Axapta2Class RawAxapta => _axapta;

        /// <summary>
        /// Checks whether the existing session can be reused for the requested identity and configuration.
        /// </summary>
        public bool Matches(string username, string configPath, string company)
        {
            return string.Equals(Username, username ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(ConfigPath, configPath ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Company, company ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates and tracks an Axapta container owned by this operation scope.
        /// </summary>
        public IAxaptaContainer CreateContainer()
        {
            EnsureNotDisposed();
            var container = _guard.ExecuteComCall(
                () => _axapta.CreateContainer(),
                _ctx,
                "CreateContainer",
                "axUser=" + Username + " company=" + Company);
            TrackAxObject(container, "CreateContainer");
            return container;
        }

        /// <summary>
        /// Calls a static AX class method without arguments and tracks the returned AX object.
        /// </summary>
        public object CallStaticClassMethod(string className, string methodName)
        {
            EnsureNotDisposed();
            return ExecuteStaticClassCall(
                className,
                methodName,
                "args=none",
                () => _axapta.CallStaticClassMethod(className, methodName));
        }

        /// <summary>
        /// Calls a static AX class method with one argument and tracks the returned AX object.
        /// </summary>
        public object CallStaticClassMethod(string className, string methodName, object arg)
        {
            EnsureNotDisposed();
            return ExecuteStaticClassCall(
                className,
                methodName,
                "args=single:" + DescribeArg(arg),
                () => _axapta.CallStaticClassMethod(className, methodName, arg));
        }

        /// <summary>
        /// Calls a static AX class method with an argument array and tracks the returned AX object.
        /// </summary>
        public object CallStaticClassMethod(string className, string methodName, object[] args)
        {
            EnsureNotDisposed();
            return ExecuteStaticClassCall(
                className,
                methodName,
                "args=array:" + (args == null ? 0 : args.Length),
                () => args == null
                    ? _axapta.CallStaticClassMethod(className, methodName)
                    : _axapta.CallStaticClassMethod(className, methodName, args));
        }

        /// <summary>
        /// Tracks an AX/COM object created by this session so cleanup runs in reverse order.
        /// </summary>
        public void TrackAxObject(object axObject, string reason)
        {
            if (!IsTrackableAxObject(axObject))
                return;

            lock (_ownedObjectsLock)
            {
                foreach (var existing in _ownedObjects)
                {
                    if (ReferenceEquals(existing, axObject))
                        return;
                }

                _ownedObjects.Add(axObject);
            }

            _guard.LogTrackedAxObject(_ctx, reason, axObject);
        }

        internal void ReleaseTrackedObjects(string reason)
        {
            List<object> snapshot;
            lock (_ownedObjectsLock)
            {
                snapshot = new List<object>(_ownedObjects);
                _ownedObjects.Clear();
            }

            for (var i = snapshot.Count - 1; i >= 0; i--)
                _guard.SafeReleaseAxObject(snapshot[i], _ctx, reason, "tracked-object-" + i);
        }

        internal Axapta2Class DetachRawAxapta()
        {
            var ax = _axapta;
            _axapta = null;
            _disposed = true;
            return ax;
        }

        private object ExecuteStaticClassCall(string className, string methodName, string detail, Func<object> call)
        {
            var operationName = (className ?? string.Empty) + "." + (methodName ?? string.Empty);
            var result = _guard.ExecuteComCall(
                call,
                _ctx,
                operationName,
                "class=" + (className ?? string.Empty) + " method=" + (methodName ?? string.Empty) + " " + detail);
            TrackAxObject(result, operationName + " result");
            return result;
        }

        private void EnsureNotDisposed()
        {
            if (_disposed || _axapta == null)
                throw new ObjectDisposedException(nameof(AxaptaComSession));
        }

        private static string DescribeArg(object arg)
        {
            if (arg == null)
                return "null";

            if (arg is object[] arr)
                return "array:" + arr.Length;

            return arg.GetType().FullName;
        }

        private static bool IsTrackableAxObject(object axObject)
        {
            if (axObject == null)
                return false;

            if (axObject is IDisposable)
                return true;

            try
            {
                return Marshal.IsComObject(axObject);
            }
            catch
            {
                return false;
            }
        }
    }
}
