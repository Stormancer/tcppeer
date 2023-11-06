using System.Collections.Concurrent;
using System.Threading;
using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Connections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Stormancer.Networking.Reliable
{
    internal sealed class ConnectionReference
    {
        private readonly long _id;
        private readonly WeakReference<ConnectionProcessor> _weakReference;
        private readonly TransportConnectionManager _transportConnectionManager;

        public ConnectionReference(long id, ConnectionProcessor connection, TransportConnectionManager transportConnectionManager)
        {
            _id = id;

            _weakReference = new WeakReference<ConnectionProcessor>(connection);
            ConnectionId = connection.NetworkConnection.ConnectionId;

            _transportConnectionManager = transportConnectionManager;
        }

        public string ConnectionId { get; }

        public bool TryGetConnection([NotNullWhen(true)] out ConnectionProcessor? connection)
        {
            return _weakReference.TryGetTarget(out connection);
        }

        public void StopTransportTracking()
        {
            _transportConnectionManager.StopTracking(_id);
        }
    }

    internal sealed class TransportConnectionManager
    {
        private readonly ConnectionManager _connectionManager;
        private readonly ConcurrentDictionary<long, ConnectionReference> _connectionReferences = new ConcurrentDictionary<long, ConnectionReference>();

        public TransportConnectionManager(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
        }

        public long GetNewConnectionId() => _connectionManager.GetNewConnectionId();

        public void AddConnection(long id, ConnectionProcessor connection)
        {
            var connectionReference = new ConnectionReference(id, connection, this);

            if (!_connectionReferences.TryAdd(id, connectionReference))
            {
                throw new ArgumentException("Unable to add specified id.", nameof(id));
            }

            _connectionManager.AddConnection(id, connectionReference);
        }

        public void RemoveConnection(long id)
        {
            if (!_connectionReferences.TryRemove(id, out _))
            {
                throw new ArgumentException("No value found for the specified id.", nameof(id));
            }

            _connectionManager.RemoveConnection(id);
        }

        // This is only called by the ConnectionManager when the connection reference becomes
        // unrooted because the application never completed.
        public void StopTracking(long id)
        {
            if (!_connectionReferences.TryRemove(id, out _))
            {
                throw new ArgumentException("No value found for the specified id.", nameof(id));
            }
        }

        public async Task<bool> CloseAllConnectionsAsync(CancellationToken token)
        {
            var closeTasks = new List<Task>();

            foreach (var kvp in _connectionReferences)
            {
                if (kvp.Value.TryGetConnection(out var connection))
                {
                    connection.RequestClose();
                    closeTasks.Add(connection.ExecutionTask);
                }
            }

            var allClosedTask = Task.WhenAll(closeTasks.ToArray());
            return await Task.WhenAny(allClosedTask, CancellationTokenAsTask(token)).ConfigureAwait(false) == allClosedTask;
        }

        public async Task<bool> AbortAllConnectionsAsync()
        {
            var abortTasks = new List<Task>();

            foreach (var kvp in _connectionReferences)
            {
                if (kvp.Value.TryGetConnection(out var connection))
                {
                    connection.NetworkConnection.Close(new ConnectionAbortedException("Connection aborted because of server shutdown."));
                    abortTasks.Add(connection.ExecutionTask);
                }
            }

            var allAbortedTask = Task.WhenAll(abortTasks.ToArray());
            return await Task.WhenAny(allAbortedTask, Task.Delay(1000)).ConfigureAwait(false) == allAbortedTask;
        }

        private static Task CancellationTokenAsTask(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(tcs.SetResult);
            return tcs.Task;
        }
    }

    internal sealed class ConnectionManager
    {
        public ConnectionManager(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<ConnectionManager>();
        }
        private long _lastConnectionId = long.MinValue;

        private readonly ConcurrentDictionary<long, ConnectionReference> _connectionReferences = new ConcurrentDictionary<long, ConnectionReference>();
        private readonly ILogger<ConnectionManager> _logger;

        public long GetNewConnectionId() => Interlocked.Increment(ref _lastConnectionId);


        public void AddConnection(long id, ConnectionReference connectionReference)
        {
            if (!_connectionReferences.TryAdd(id, connectionReference))
            {
                throw new ArgumentException("Unable to add connection.", nameof(id));
            }
        }

        public void RemoveConnection(long id)
        {
            if (!_connectionReferences.TryRemove(id, out var reference))
            {
                throw new ArgumentException("Unable to remove connection.", nameof(id));
            }

            if (reference.TryGetConnection(out var connection))
            {
                connection.Complete();
            }
        }

        public void Walk(Action<ConnectionProcessor> callback)
        {
            foreach (var kvp in _connectionReferences)
            {
                var reference = kvp.Value;

                if (reference.TryGetConnection(out var connection))
                {
                    callback(connection);
                }
                else if (_connectionReferences.TryRemove(kvp.Key, out reference))
                {
                    // It's safe to modify the ConcurrentDictionary in the foreach.
                    // The connection reference has become unrooted because the application never completed.
                    PeerLogs.ApplicationNeverCompleted(_logger,reference.ConnectionId);
                    reference.StopTransportTracking();
                }

            }
        }


    }
}