
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
namespace Stormancer.Networking.Reliable
{
    /// <summary>
    /// Provides a way to configures a TCP Peer instance.
    /// </summary>
    public interface IPeerApiConfigurator
    {
        /// <summary>
        /// Called to configure the TCP Peer.
        /// </summary>
        /// <param name="builder"></param>
        void OnRegisteringOperations(PeerOperationsBuilder builder);

    }

    /// <summary>
    /// Configuration of a peer.
    /// </summary>
    public class PeerOptions
    {
        /// <summary>
        /// Gets or sets the local id of the <see cref="LocalPeer"/> instance, used for logging purpose.
        /// </summary>
        public string Name { get; set; } = "peer";
        public Func<IEnumerable<IPeerApiConfigurator>> OperationConfiguratorsGetter { get; set; }
        public Action<PeerRequest> OnSendingRequest { get; set; }
        public Func<RequestContext, Task> OnRequestReceived { get; set; }
        public Action<RemotePeer> OnConnected { get; set; }
        public Action<RemotePeer, DisconnectionReason> OnDisconnected { get; set; }

        public MemoryPool<byte> MemoryPool { get; set; } = MemoryPool<byte>.Shared;
        public void SetMetadata(ReadOnlySpan<byte> metadata)
        {
            
            var metadataMemoryOwner = MemoryPool.Rent(metadata.Length);
            metadata.CopyTo(metadataMemoryOwner.Memory.Span);

            Metadata?.Dispose();
            Metadata = new PeerMetadata(PeerId.CreateNew(),metadataMemoryOwner, metadataMemoryOwner.Memory.Slice(0,metadata.Length));
        }

        /// <summary>
        /// Gets or sets the default factory to create <see cref="EndPointConfig"/> objects for clients.
        /// </summary>
        public Func<EndPointConfig> DefaultClientEndpointConfigFactory { get; set; } = () => new EndPointConfig();

        /// <summary>
        /// Gets or sets the metadata of the local peer.
        /// </summary>
        public PeerMetadata? Metadata { get; set; }

        /// <summary>
        /// Gets the options related to the transports
        /// </summary>
        public TransportsOptions TransportsOptions { get; } = new TransportsOptions();

        /// <summary>
        /// Gets or sets the logger factory to use.
        /// </summary>
        /// <remarks>
        /// Defaults to <see cref="NullLoggerFactory"/>.
        /// </remarks>
        public ILoggerFactory LoggerFactory { get; set; } = new NullLoggerFactory();

       
    }

    /// <summary>
    /// Configuration of the Peer transports.
    /// </summary>
    public class TransportsOptions
    {
        private readonly List<Func<ConnectionDelegate, ConnectionDelegate>> _middleware = new List<Func<ConnectionDelegate, ConnectionDelegate>>();

        /// <summary>
        /// Gets the endpoints.
        /// </summary>
        public List<TransportConfiguration> Endpoints { get; } = new List<TransportConfiguration>();


        /// <summary>
        /// Gets the features set on the client on startup.
        /// </summary>
        public IFeatureCollection Features { get; } = new FeatureCollection();

        /// <summary>
        /// Adds a middleware delegate to the connection pipeline.
        /// </summary>
        /// <param name="middleware">The middleware delegate.</param>
        /// <returns>The <see cref="IConnectionBuilder"/>.</returns>
        public TransportsOptions Use(Func<ConnectionDelegate, ConnectionDelegate> middleware)
        {
            _middleware.Add(middleware);
            return this;
        }

        /// <summary>
        /// Builds the <see cref="ConnectionDelegate"/>.
        /// </summary>
        /// <returns>The <see cref="ConnectionDelegate"/>.</returns>
        public ConnectionDelegate Build()
        {
            ConnectionDelegate app = context =>
            {
                return Task.CompletedTask;
            };

            for (var i = _middleware.Count - 1; i >= 0; i--)
            {
                var component = _middleware[i];
                app = component(app);
            }

            return app;
        }
    }

    /// <summary>
    /// Configuration of a transport.
    /// </summary>
    public class TransportConfiguration
    {
        public EndPoint BindingEndpoint { get; set; }
    }

    /// <summary>
    /// Configures the operations provided by the peer.
    /// </summary>
    public class PeerOperationsBuilder
    {
      
        private readonly List<OperationHandler> _handlers;

        internal PeerOperationsBuilder(List<OperationHandler> handlers, PeerMetadata peerMetadata)
        {
            _handlers = handlers;
            this.PeerMetadata = peerMetadata;
        }

        /// <summary>
        /// Metadata associated with the peer
        /// </summary>
        public PeerMetadata PeerMetadata { get; }

        /// <summary>
        /// Adds a remotely accessible operation to the peer.
        /// </summary>
        /// <param name="operation"></param>
        /// <param name="handler"></param>
        /// <returns></returns>
        public PeerOperationsBuilder ConfigureOperation(string operation, Func<RequestContext, ValueTask> handler)
        {
            _handlers.Add(new OperationHandler(new OperationId(operation), handler));
            return this;
        }
    }
}
