// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable;

internal sealed class ConnectionDispatcher
{
    private readonly Func<NetworkConnection, Task> _connectionDelegate;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ConnectionDispatcher> _logger;
    private readonly TransportConnectionManager _transportConnectionManager;
    private readonly TaskCompletionSource _acceptLoopTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConnectionDispatcher(Func<NetworkConnection, Task> connectionDelegate, ILoggerFactory loggerFactory,TransportConnectionManager transportConnectionManager)
    {
       
        _connectionDelegate = connectionDelegate;
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<ConnectionDispatcher>();
        _transportConnectionManager = transportConnectionManager;
    }

  

    public Task StartAcceptingConnections(IConnectionListener listener)
    {
        ThreadPool.UnsafeQueueUserWorkItem(StartAcceptingConnectionsCore, listener, preferLocal: false);
        return _acceptLoopTcs.Task;
    }

    private void StartAcceptingConnectionsCore(IConnectionListener listener)
    {
        // REVIEW: Multiple accept loops in parallel?
        _ = AcceptConnectionsAsync();

        async Task AcceptConnectionsAsync()
        {
            try
            {
                while (true)
                {
                    var connection = await listener.AcceptAsync();

                    if (connection == null)
                    {
                        // We're done listening
                        break;
                    }

                    // Add the connection to the connection manager before we queue it for execution
                    var id = _transportConnectionManager.GetNewConnectionId();

                    

                    var processor = new ConnectionProcessor(
                        id, _transportConnectionManager, _connectionDelegate, connection, _loggerFactory);

                    _transportConnectionManager.AddConnection(id, processor);

                    PeerLogs.ConnectionAccepted(_logger,connection.ConnectionId);
                    
                    ThreadPool.UnsafeQueueUserWorkItem(processor, preferLocal: false);
                }
            }
            catch (Exception ex)
            {
                // REVIEW: If the accept loop ends should this trigger a server shutdown? It will manifest as a hang
                _logger.LogCritical(0, ex, "The connection listener failed to accept any new connections.");
            }
            finally
            {
                _acceptLoopTcs.TrySetResult();
            }
        }
    }
}
