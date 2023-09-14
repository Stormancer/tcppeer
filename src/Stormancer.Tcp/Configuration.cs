
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace Stormancer.Tcp
{
    /// <summary>
    /// Provides a way to configures a TCP Peer instance.
    /// </summary>
    public interface ITcpPeerConfigurator
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
        public Func<IEnumerable<ITcpPeerConfigurator>> OperationConfiguratorsGetter { get; set; }
        public Action<ClientRequest> OnSendingRequest { get; set; }
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

        internal PeerMetadata? Metadata { get; set; }

    }
    /// <summary>
    /// Configures the operations provided by the peer.
    /// </summary>
    public class PeerOperationsBuilder
    {
        private readonly HandlerStore server;
        internal PeerOperationsBuilder(HandlerStore server, PeerMetadata peerMetadata)
        {
            this.server = server;
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
        public PeerOperationsBuilder ConfigureOperation(string operation, Func<RequestContext, Task> handler)
        {
            server.Add(operation, handler);
            return this;
        }
    }
}
