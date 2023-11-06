using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    internal class ConnectionProcessor : IThreadPoolWorkItem, IConnectionCompleteFeature, IConnectionLifetimeNotificationFeature
    {
        private readonly TransportConnectionManager _transportConnectionManager;
        private readonly Func<NetworkConnection, Task> _connectionDelegate;
        private readonly ILogger<ConnectionProcessor> _logger;


        private Stack<KeyValuePair<Func<object, Task>, object>>? _onCompleted;
        private bool _completed;
        private readonly CancellationTokenSource _connectionClosingCts = new CancellationTokenSource();
        private readonly TaskCompletionSource _completionTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConnectionProcessor(long id, TransportConnectionManager transportConnectionManager, Func<NetworkConnection, Task> connectionDelegate, NetworkConnection connection, ILoggerFactory loggerFactory)
        {
            _transportConnectionManager = transportConnectionManager;
            _connectionDelegate = connectionDelegate;       
            _logger = loggerFactory.CreateLogger<ConnectionProcessor>();

            Id = id;
            ConnectionClosedRequested = _connectionClosingCts.Token;
            NetworkConnection = connection;

            connection.Features.Set<IConnectionCompleteFeature>(this);
            connection.Features.Set<IConnectionLifetimeNotificationFeature>(this);
        }

        /// <summary>
        /// Gets the underlying connection.
        /// </summary>
        public NetworkConnection NetworkConnection { get; }

        public CancellationToken ConnectionClosedRequested { get; }

        public Task ExecutionTask => _completionTcs.Task;
        public long Id { get; }

        public void Execute()
        {
            _ = ExecuteAsync();
        }

        internal async Task ExecuteAsync()
        {
            var connectionContext = NetworkConnection;
            Exception? unhandledException = null;



            try
            {
                PeerLogs.ConnectionStart(_logger, connectionContext.ConnectionId);

                try
                {
                    await _connectionDelegate(connectionContext);
                }
                catch (Exception ex)
                {
                    unhandledException = ex;
                    _logger.LogError(0, ex, "Unhandled exception while processing {ConnectionId}.", connectionContext.ConnectionId);
                }

            }
            finally
            {
                await FireOnCompletedAsync();

                PeerLogs.ConnectionStop(_logger, connectionContext.ConnectionId);

                // Dispose the transport connection, this needs to happen before removing it from the
                // connection manager so that we only signal completion of this connection after the transport
                // is properly torn down.
                await connectionContext.DisposeAsync();

                _transportConnectionManager.RemoveConnection(Id);
            }
        }

        public Task FireOnCompletedAsync()
        {
            if (_completed)
            {
                throw new InvalidOperationException("The connection is already complete.");
            }

            _completed = true;
            var onCompleted = _onCompleted;

            if (onCompleted == null || onCompleted.Count == 0)
            {
                return Task.CompletedTask;
            }

            return CompleteAsyncMayAwait(onCompleted);
        }

        private Task CompleteAsyncMayAwait(Stack<KeyValuePair<Func<object, Task>, object>> onCompleted)
        {
            while (onCompleted.TryPop(out var entry))
            {
                try
                {
                    var task = entry.Key.Invoke(entry.Value);
                    if (!task.IsCompletedSuccessfully)
                    {
                        return CompleteAsyncAwaited(task, onCompleted);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred running an IConnectionCompleteFeature.OnCompleted callback.");
                }
            }

            return Task.CompletedTask;
        }

        private async Task CompleteAsyncAwaited(Task currentTask, Stack<KeyValuePair<Func<object, Task>, object>> onCompleted)
        {
            try
            {
                await currentTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred running an IConnectionCompleteFeature.OnCompleted callback.");
            }

            while (onCompleted.TryPop(out var entry))
            {
                try
                {
                    await entry.Key.Invoke(entry.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred running an IConnectionCompleteFeature.OnCompleted callback.");
                }
            }
        }

        public void Complete()
        {
            _completionTcs.TrySetResult();

            _connectionClosingCts.Dispose();
        }

        public void RequestClose()
        {
            try
            {
                _connectionClosingCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // There's a race where the token could be disposed
                // swallow the exception and no-op
            }
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
            if (_completed)
            {
                throw new InvalidOperationException("The connection is already complete.");
            }

            if (_onCompleted == null)
            {
                _onCompleted = new Stack<KeyValuePair<Func<object, Task>, object>>();
            }
            _onCompleted.Push(new KeyValuePair<Func<object, Task>, object>(callback, state));
        }
    }
}
