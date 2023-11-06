using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    /// <summary>
    /// Endpoint configuration
    /// </summary>
    public class EndPointConfig
    {
        /// <summary>
        /// Gets or sets the initial features set on the <see cref="NetworkConnection"/>.
        /// </summary>
        public IFeatureCollection? Features { get; set; }
    }



    /// <summary>
    /// A function that can process a connection.
    /// </summary>
    /// <param name="connection"></param>
    /// <returns></returns>
    public delegate Task ConnectionDelegate(NetworkConnection connection);

    internal class TransportManager
    {
        public TransportManager(
        IEnumerable<IConnectionListenerFactory> transportFactories,
        IEnumerable<IClientConnectionFactory> clientFactories,
        ConnectionManager connectionManager,
        ILoggerFactory loggerFactory)
        {
            _transportFactories = transportFactories;
            _clientFactories = clientFactories;
            _connectionManager = connectionManager;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<TransportManager>();
        }
        private sealed class ActiveTransport : IAsyncDisposable
        {
            public ActiveTransport(IAsyncDisposable transport, Task acceptLoopTask, TransportConnectionManager transportConnectionManager, EndPointConfig? endpointConfig = null)
            {
                TransportResource = transport;
                AcceptLoopTask = acceptLoopTask;
                TransportConnectionManager = transportConnectionManager;
                EndpointConfig = endpointConfig;
            }

            public IAsyncDisposable TransportResource { get; }
            public Task AcceptLoopTask { get; }
            public TransportConnectionManager TransportConnectionManager { get; }

            public EndPointConfig? EndpointConfig { get; }

            public async Task UnbindAsync(CancellationToken cancellationToken)
            {
                await TransportResource.DisposeAsync().ConfigureAwait(false);
                await AcceptLoopTask.ConfigureAwait(false);
            }

            public ValueTask DisposeAsync()
            {
                return TransportResource.DisposeAsync();
            }
        }
        private readonly IEnumerable<IConnectionListenerFactory> _transportFactories;
        private readonly IEnumerable<IClientConnectionFactory> _clientFactories;
        private readonly ConnectionManager _connectionManager;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<TransportManager> _logger;
        private readonly List<ActiveTransport> _transports = new();



        public async Task ConnectAsync(EndPoint endPoint, ConnectionDelegate connectionDelegate, EndPointConfig? endpointConfig, CancellationToken cancellationToken)
        {
            if (!_transportFactories.Any())
            {
                throw new InvalidOperationException($"Cannot connect to {endPoint} with {nameof(ConnectionDelegate)} no {nameof(IConnectionListenerFactory)} is registered.");
            }

            foreach (var transportFactory in _clientFactories)
            {

                if (transportFactory.CanBind(endPoint))
                {
                    var connection = await transportFactory.ConnectAsync(endPoint,endpointConfig, cancellationToken).ConfigureAwait(false);

                    var transportConnectionManager = new TransportConnectionManager(_connectionManager);

                    var id = transportConnectionManager.GetNewConnectionId();

                    var processor = new ConnectionProcessor(id, transportConnectionManager, c => connectionDelegate(c), connection, _loggerFactory);

                    transportConnectionManager.AddConnection(id, processor);

                    ThreadPool.UnsafeQueueUserWorkItem(processor, preferLocal: false);

                    _transports.Add(new ActiveTransport(connection,Task.CompletedTask,transportConnectionManager,endpointConfig));

                    return;
                }
            }
            throw new InvalidOperationException($"No registered {nameof(IConnectionListenerFactory)} supports endpoint {endPoint.GetType().Name}: {endPoint}");
        }

        public async Task<EndPoint> BindAsync(EndPoint endPoint, ConnectionDelegate connectionDelegate, EndPointConfig? endpointConfig, CancellationToken cancellationToken)
        {
            if (!_transportFactories.Any())
            {
                throw new InvalidOperationException($"Cannot bind with {nameof(ConnectionDelegate)} no {nameof(IConnectionListenerFactory)} is registered.");
            }

            foreach (var transportFactory in _transportFactories)
            {

                if (transportFactory.CanBind(endPoint))
                {
                    var transport = await transportFactory.BindAsync(endPoint,endpointConfig, cancellationToken).ConfigureAwait(false);
                    StartAcceptLoop(transport, c => connectionDelegate(c), endpointConfig);
                    return transport.LocalEndpoint;
                }
            }
            throw new InvalidOperationException($"No registered {nameof(IConnectionListenerFactory)} supports endpoint {endPoint.GetType().Name}: {endPoint}");
        }

        private void StartAcceptLoop(IConnectionListener connectionListener, Func<NetworkConnection, Task> connectionDelegate, EndPointConfig? endpointConfig)
        {
            //Create manager for the connections of this transport.
            var transportConnectionManager = new TransportConnectionManager(_connectionManager);
            var dispatcher = new ConnectionDispatcher(connectionDelegate, _loggerFactory, transportConnectionManager);
            var acceptLoopTask = dispatcher.StartAcceptingConnections(connectionListener);
            _transports.Add(new ActiveTransport(connectionListener, acceptLoopTask, transportConnectionManager, endpointConfig));
        }


        public Task StopEndpointsAsync(List<EndPointConfig> endpointsToStop, CancellationToken cancellationToken)
        {
            var transportsToStop = new List<ActiveTransport>();
            foreach (var t in _transports)
            {
                if (t.EndpointConfig is not null && endpointsToStop.Contains(t.EndpointConfig))
                {
                    transportsToStop.Add(t);
                }
            }
            return StopTransportsAsync(transportsToStop, cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return StopTransportsAsync(new List<ActiveTransport>(_transports), cancellationToken);
        }

        private async Task StopTransportsAsync(List<ActiveTransport> transportsToStop, CancellationToken cancellationToken)
        {
            var tasks = new Task[transportsToStop.Count];

            for (int i = 0; i < transportsToStop.Count; i++)
            {
                tasks[i] = transportsToStop[i].UnbindAsync(cancellationToken);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            async Task StopTransportConnection(ActiveTransport transport)
            {
                if (!await transport.TransportConnectionManager.CloseAllConnectionsAsync(cancellationToken).ConfigureAwait(false))
                {
                    PeerLogs.NotAllConnectionsClosedGracefully(_logger);

                    if (!await transport.TransportConnectionManager.AbortAllConnectionsAsync().ConfigureAwait(false))
                    {
                        PeerLogs.NotAllConnectionsAborted(_logger);
                    }
                }
            }

            for (int i = 0; i < transportsToStop.Count; i++)
            {
                tasks[i] = StopTransportConnection(transportsToStop[i]);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            for (int i = 0; i < transportsToStop.Count; i++)
            {
                tasks[i] = transportsToStop[i].DisposeAsync().AsTask();
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            foreach (var transport in transportsToStop)
            {
                _transports.Remove(transport);
            }
        }

    }
}
