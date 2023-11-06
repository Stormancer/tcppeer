using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable.Features
{
    /// <summary>
    /// Provides info about the connection
    /// </summary>
    public interface IPeerConnectionFeature
    {
        /// <summary>
        /// Gets the local endpoint of the connection.
        /// </summary>
        public EndPoint LocalEndPoint { get; }

        /// <summary>
        /// Gets the remote endpoint of the connection.
        /// </summary>
        public EndPoint RemoteEndPoint { get; }

        /// <summary>
        /// Gets the metadata of the local peer.
        /// </summary>
        public PeerMetadata? LocalPeerMetadata { get; }

        /// <summary>
        /// Gets the metadata of the remote peer.
        /// </summary>
        public PeerMetadata? RemotePeerMetadata { get; }

        /// <summary>
        /// Gets a boolean indicating whether the local peer is a client.
        /// </summary>
        public bool IsClient { get; }

        /// <summary>
        /// Wait asynchronously that the peer exchanged their metadata.
        /// </summary>
        /// <returns></returns>
        public Task WhenMetadataExchangedAsync();
    }


    internal class PeerConnectionFeature : IPeerConnectionFeature
    {
     
        public PeerMetadata? LocalPeerMetadata { get; internal set; }
        public PeerMetadata? RemotePeerMetadata { get; internal set; }

        public EndPoint? LocalEndPoint { get; internal set; }

        public EndPoint? RemoteEndPoint { get; internal set; }
        public bool IsClient { get; internal set; }

        private readonly TaskCompletionSource _metadataExchangedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task WhenMetadataExchangedAsync()
        {
            return _metadataExchangedTcs.Task;
        }

        internal void SetMetadataExchanged() => _metadataExchangedTcs.SetResult();
    }
}
