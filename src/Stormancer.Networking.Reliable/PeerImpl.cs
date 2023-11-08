using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Stormancer.Networking.Reliable.Features;
using Stormancer.Networking.Reliable.Requests;
using Stormancer.Networking.Reliable.Routing;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    internal class PeerImpl : IAsyncDisposable
    {
        private readonly PeerMetadata _localPeer;
        private readonly TransportsOptions _transportsConfiguration;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<PeerImpl> _logger;
        private readonly ConnectionManager _connectionManager;
        private readonly TransportManager _transportManager;

        public PeerImpl(
            PeerMetadata localPeer
            , TransportsOptions transportsConfiguration
            , MemoryPool<byte> memoryPool
            , ServiceContext serviceContext)
        {
            _localPeer = localPeer;
            _transportsConfiguration = transportsConfiguration;

            _loggerFactory = serviceContext.LoggerFactory;
            _logger = _loggerFactory.CreateLogger<PeerImpl>();

            _connectionManager = serviceContext.ConnectionManager;
            _transportManager = serviceContext.TransportManager;

            _transportsConfiguration.Features.Set<IMemoryPoolFeature>(new DefaultMemoryPoolFeature(memoryPool));
            _transportsConfiguration
                .UseMetadataExchange()
                .UseRouteTableMiddleware(serviceContext.RouteTable)
                .UseRequestServer(serviceContext);


        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var endpointConfig = new EndPointConfig { Features = new FeatureCollection(_transportsConfiguration.Features) };

            var peerConnectionFeature = new PeerConnectionFeature() { LocalPeerMetadata = _localPeer };
            endpointConfig.Features.Set<IPeerConnectionFeature>(peerConnectionFeature);
            
            var connectionDelegate = _transportsConfiguration.Build();


            foreach (var transport in _transportsConfiguration.Endpoints)
            {
                try
                {
                   
                    await _transportManager.BindAsync(transport.BindingEndpoint, connectionDelegate, endpointConfig, cancellationToken);
                }
                catch (Exception ex) when (ex is not IOException && ex is not NotSupportedException)
                {
                    _logger.LogError(ex, "Failed to bind to {boundAddress}", transport.BindingEndpoint);
                }
            }
        }

        public async Task ConnectAsync(EndPoint endPoint, CancellationToken cancellationToken)
        {
            var connectionDelegate = _transportsConfiguration.Build();
            var endPointConfig = new EndPointConfig { Features = new FeatureCollection(_transportsConfiguration.Features) };
            var peerConnectionFeature = new PeerConnectionFeature() { LocalPeerMetadata = _localPeer };
            endPointConfig.Features.Set<IPeerConnectionFeature>(peerConnectionFeature);
            
            await _transportManager.ConnectAsync(endPoint, connectionDelegate, endPointConfig, cancellationToken);

            var feature = endPointConfig.Features.Get<IPeerConnectionFeature>();
            Debug.Assert(feature != null);

            await feature.WhenStartingProcessingData().WaitAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _transportManager.StopAsync(CancellationToken.None);
        }

     
    }
}
