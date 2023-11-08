using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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

        internal RemotePeer(PeerMetadata metadata, EndPoint endPoint)
        {
            Metadata = metadata;
            Endpoint = endPoint;
            Id = metadata.PeerId;
        }


        /// <summary>
        /// Gets metadata 
        /// </summary>
        public PeerMetadata Metadata { get; }

        /// <summary>
        /// Gets the id of the peer in the cluster.
        /// </summary>
        public PeerId Id { get; }

        /// <summary>
        /// Gets the endpoint used to connect to the peer.
        /// </summary>
        public EndPoint Endpoint {get;}
    }
}
