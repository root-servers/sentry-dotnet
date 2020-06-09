using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Sentry.Extensibility;
using Sentry.Infrastructure;
using Sentry.Internal;
using Sentry.Protocol;

namespace Sentry
{
    /// <summary>
    /// Sentry SDK entrypoint
    /// </summary>
    /// <remarks>
    /// This is a façade to the SDK instance.
    /// It allows safe static access to a client and scope management.
    /// When the SDK is uninitialized, calls to this class result in no-op so no callbacks are invoked.
    /// </remarks>
    public static class SentrySdk
    {

        /// <summary>
        /// The Main Hub or NoOp if Sentry is disabled.
        /// </summary>
        private static readonly AsyncLocal<IHub> _currentHub = new AsyncLocal<IHub>();
        /// <summary>
        /// The Main Hub or NoOp if Sentry is disabled.
        /// </summary>
        private static IHub _mainHud = DisabledHub.Instance;

        /// <summary>
        /// Default value for globalHubMode is false.
        /// </summary>
        private  const bool _globalHudDefaultMode = false;

        /// <summary>
        /// whether to use a single (global) Hub as opposed to one per thread.
        /// </summary>
        private static bool _globalHudMode = _globalHudDefaultMode;

        /// <summary>
        /// Last event id recorded in the current scope
        /// </summary>
        public static SentryId LastEventId { [DebuggerStepThrough] get => _mainHud.LastEventId; }

        /// <summary>
        /// Returns the current (threads) hub, if none, clones the mainHub and returns it.
        /// </summary>
        /// <returns>A Hub</returns>
        private static IHub GetCurrentHub()
        {
            if (_globalHudMode)
            {
                return _mainHud;
            }
            IHub hub = _currentHub.Value;
            if (hub == null)
            {
                hub = Interlocked.Exchange(ref _mainHud, DisabledHub.Instance);
                _currentHub.Value = hub;
            }
            return hub;

        }

        /// <summary>
        /// Initializes the SDK while attempting to locate the DSN
        /// </summary>
        /// <remarks>
        /// If the DSN is not found, the SDK will not change state.
        /// </remarks>
        public static IDisposable Init() => Init(DsnLocator.FindDsnStringOrDisable(), _globalHudDefaultMode);


        /// <summary>
        /// Initializes the SDK while attempting to locate the DSN
        /// </summary>
        /// <remarks>
        /// If the DSN is not found, the SDK will not change state.
        /// </remarks>
        public static IDisposable Init(bool globalHudMode = _globalHudDefaultMode) => Init(DsnLocator.FindDsnStringOrDisable(), globalHudMode);


        /// <summary>
        /// Initializes the SDK with the specified DSN
        /// </summary>
        /// <remarks>
        /// An empty string is interpreted as a disabled SDK
        /// </remarks>
        /// <seealso href="https://docs.sentry.io/clientdev/overview/#usage-for-end-users"/>
        /// <param name="dsn">The dsn</param>
        public static IDisposable Init(string dsn)
            => string.IsNullOrWhiteSpace(dsn)
                ? DisabledHub.Instance
                : Init(c => c.Dsn = new Dsn(dsn), _globalHudDefaultMode);

        /// <summary>
        /// Initializes the SDK with the specified DSN
        /// </summary>
        /// <remarks>
        /// An empty string is interpreted as a disabled SDK
        /// </remarks>
        /// <seealso href="https://docs.sentry.io/clientdev/overview/#usage-for-end-users"/>
        /// <param name="dsn">The dsn</param>
        /// <param name="globalHudMode">whether to use a single (global) Hub as opposed to one per thread.</param>
        public static IDisposable Init(string dsn, bool globalHudMode = _globalHudDefaultMode )
            => string.IsNullOrWhiteSpace(dsn)
                ? DisabledHub.Instance
                : Init(c => c.Dsn = new Dsn(dsn), globalHudMode);

        /// <summary>
        /// Initializes the SDK with the specified DSN
        /// </summary>
        /// <param name="dsn">The dsn</param>
        public static IDisposable Init(Dsn dsn) => Init(c => c.Dsn = dsn, _globalHudDefaultMode);

        /// <summary>
        /// Initializes the SDK with an optional configuration options callback.
        /// </summary>
        /// <param name="configureOptions">The configure options.</param>
        /// <param name="globalHudMode">whether to use a single (global) Hub as opposed to one per thread.</param>
        public static IDisposable Init(Action<SentryOptions> configureOptions, bool globalHudMode = _globalHudDefaultMode)
        {
            var options = new SentryOptions();
            configureOptions?.Invoke(options);

            return Init(options, globalHudMode);
        }

        /// <summary>
        /// Initializes the SDK with the specified options instance
        /// </summary>
        /// <param name="options">The options instance</param>
        /// <param name="globalHudMode">whether to use a single (global) Hub as opposed to one per thread.</param>
        /// <remarks>
        /// Used by integrations which have their own delegates
        /// </remarks>
        /// <returns>A disposable to close the SDK.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static IDisposable Init(SentryOptions options, bool globalHudMode = _globalHudDefaultMode)
        {
            if (options.Dsn == null)
            {
                if (!Dsn.TryParse(DsnLocator.FindDsnStringOrDisable(), out var dsn))
                {
                    options.DiagnosticLogger?.LogWarning("Init was called but no DSN was provided nor located. Sentry SDK will be disabled.");
                    return DisabledHub.Instance;
                }
                options.Dsn = dsn;
            }
            _globalHudMode = globalHudMode;

            return UseHub(new Hub(options));
        }

        internal static IDisposable UseHub(IHub hub)
        {
            var oldHub = Interlocked.Exchange(ref _mainHud, hub);
            (oldHub as IDisposable)?.Dispose();
            return new DisposeHandle(hub);
        }

        /// <summary>
        /// Flushes events queued up.
        /// </summary>
        [DebuggerStepThrough]
        public static Task FlushAsync(TimeSpan timeout) => GetCurrentHub().FlushAsync(timeout);

        /// <summary>
        /// Close the SDK
        /// </summary>
        /// <remarks>
        /// Flushes the events and disables the SDK.
        /// This method is mostly used for testing the library since
        /// Init returns a IDisposable that can be used to shutdown the SDK.
        /// </remarks>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Close()
        {
            var oldHub = Interlocked.Exchange(ref _mainHud, DisabledHub.Instance);
            (oldHub as IDisposable)?.Dispose();
        }

        private class DisposeHandle : IDisposable
        {
            private IHub _localHub;
            public DisposeHandle(IHub hub) => _localHub = hub;

            public void Dispose()
            {
                _ = Interlocked.CompareExchange(ref _mainHud, DisabledHub.Instance, _localHub);
                (_localHub as IDisposable)?.Dispose();
                _localHub = null;
            }
        }

        /// <summary>
        /// Whether the SDK is enabled or not
        /// </summary>
        public static bool IsEnabled { [DebuggerStepThrough] get => GetCurrentHub().IsEnabled; }

        /// <summary>
        /// Creates a new scope that will terminate when disposed
        /// </summary>
        /// <remarks>
        /// Pushes a new scope while inheriting the current scope's data.
        /// </remarks>
        /// <typeparam name="TState"></typeparam>
        /// <param name="state">A state object to be added to the scope</param>
        /// <returns>A disposable that when disposed, ends the created scope.</returns>
        [DebuggerStepThrough]
        public static IDisposable PushScope<TState>(TState state)
        {
            // pushScope is no-op in global hub mode
            if (!_globalHudMode)
            {
                return GetCurrentHub().PushScope(state);
            }
            return null;
        }
        /// <summary>
        /// Creates a new scope that will terminate when disposed
        /// </summary>
        /// <returns>A disposable that when disposed, ends the created scope.</returns>
        [DebuggerStepThrough]
        public static IDisposable PushScope()
        {
            // pushScope is no-op in global hub mode
            if (!_globalHudMode)
            {
                return GetCurrentHub().PushScope();
            }
            return null;
        }
        /// <summary>
        /// Binds the client to the current scope.
        /// </summary>
        /// <param name="client">The client.</param>
        [DebuggerStepThrough]
        public static void BindClient(ISentryClient client) => GetCurrentHub().BindClient(client);

        /// <summary>
        /// Adds a breadcrumb to the current Scope
        /// </summary>
        /// <param name="message">
        /// If a message is provided it’s rendered as text and the whitespace is preserved.
        /// Very long text might be abbreviated in the UI.</param>
        /// <param name="type">
        /// The type of breadcrumb.
        /// The default type is default which indicates no specific handling.
        /// Other types are currently http for HTTP requests and navigation for navigation events.
        /// <seealso href="https://docs.sentry.io/clientdev/interfaces/breadcrumbs/#breadcrumb-types"/>
        /// </param>
        /// <param name="category">
        /// Categories are dotted strings that indicate what the crumb is or where it comes from.
        /// Typically it’s a module name or a descriptive string.
        /// For instance ui.click could be used to indicate that a click happened in the UI or flask could be used to indicate that the event originated in the Flask framework.
        /// </param>
        /// <param name="data">
        /// Data associated with this breadcrumb.
        /// Contains a sub-object whose contents depend on the breadcrumb type.
        /// Additional parameters that are unsupported by the type are rendered as a key/value table.
        /// </param>
        /// <param name="level">Breadcrumb level.</param>
        /// <seealso href="https://docs.sentry.io/clientdev/interfaces/breadcrumbs/"/>
        [DebuggerStepThrough]
        public static void AddBreadcrumb(
            string message,
            string category = null,
            string type = null,
            IDictionary<string, string> data = null,
            BreadcrumbLevel level = default)
            => GetCurrentHub().AddBreadcrumb(message, category, type, data, level);

        /// <summary>
        /// Adds a breadcrumb to the current scope
        /// </summary>
        /// <remarks>
        /// This overload is intended to be used by integrations only.
        /// The objective is to allow better testability by allowing control of the timestamp set to the breadcrumb.
        /// </remarks>
        /// <param name="clock">An optional <see cref="ISystemClock"/></param>
        /// <param name="message">The message.</param>
        /// <param name="type">The type.</param>
        /// <param name="category">The category.</param>
        /// <param name="data">The data.</param>
        /// <param name="level">The level.</param>
        [DebuggerStepThrough]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void AddBreadcrumb(
            ISystemClock clock,
            string message,
            string category = null,
            string type = null,
            IDictionary<string, string> data = null,
            BreadcrumbLevel level = default)
            => GetCurrentHub().AddBreadcrumb(clock, message, category, type, data, level);

        /// <summary>
        /// Runs the callback with a new scope which gets dropped at the end
        /// </summary>
        /// <remarks>
        /// Pushes a new scope, runs the callback, pops the scope.
        /// </remarks>
        /// <see href="https://docs.sentry.io/learn/scopes/?platform=csharp#local-scopes"/>
        /// <param name="scopeCallback">The callback to run with the one time scope.</param>
        [DebuggerStepThrough]
        public static void WithScope(Action<Scope> scopeCallback)
            => GetCurrentHub().WithScope(scopeCallback);

        /// <summary>
        /// Configures the scope through the callback.
        /// </summary>
        /// <param name="configureScope">The configure scope callback.</param>
        [DebuggerStepThrough]
        public static void ConfigureScope(Action<Scope> configureScope)
            => GetCurrentHub().ConfigureScope(configureScope);

        /// <summary>
        /// Configures the scope asynchronously
        /// </summary>
        /// <param name="configureScope">The configure scope callback.</param>
        /// <returns>The Id of the event</returns>
        [DebuggerStepThrough]
        public static Task ConfigureScopeAsync(Func<Scope, Task> configureScope)
            => GetCurrentHub().ConfigureScopeAsync(configureScope);

        /// <summary>
        /// Captures the event.
        /// </summary>
        /// <param name="evt">The event.</param>
        /// <returns>The Id of the event</returns>
        [DebuggerStepThrough]
        public static SentryId CaptureEvent(SentryEvent evt)
            => GetCurrentHub().CaptureEvent(evt);

        /// <summary>
        /// Captures the event using the specified scope.
        /// </summary>
        /// <param name="evt">The event.</param>
        /// <param name="scope">The scope.</param>
        /// <returns>The Id of the event</returns>
        [DebuggerStepThrough]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static SentryId CaptureEvent(SentryEvent evt, Scope scope)
            => GetCurrentHub().CaptureEvent(evt, scope);

        /// <summary>
        /// Captures the exception.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <returns>The Id of the event</returns>
        [DebuggerStepThrough]
        public static SentryId CaptureException(Exception exception)
            => GetCurrentHub().CaptureException(exception);

        /// <summary>
        /// Captures the message.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="level">The message level.</param>
        /// <returns>The Id of the event</returns>
        [DebuggerStepThrough]
        public static SentryId CaptureMessage(string message, SentryLevel level = SentryLevel.Info)
            => GetCurrentHub().CaptureMessage(message, level);
    }
}
