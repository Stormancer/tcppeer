using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    /// <summary>
    /// Structure describing disconnection reasons
    /// </summary>
    /// <param name="Reason"></param>
    /// <param name="Error"></param>
    public record DisconnectionReason(string Reason, Exception? Error);

    /// <summary>
    /// A remote peer 
    /// </summary>
    public class RemotePeer
    {

        internal RemotePeer(PeerMetadata metadata, PeerId id)
        {
            Metadata = metadata;
            Id = id;
        }


        /// <summary>
        /// Gets metadata 
        /// </summary>
        public PeerMetadata Metadata { get; }

        /// <summary>
        /// Gets th id of the peer in the cluster.
        /// </summary>
        public PeerId Id { get; }
    }
}
