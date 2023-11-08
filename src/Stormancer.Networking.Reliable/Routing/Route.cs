using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable.Routing
{
    internal class Route
    {
        public Route(PeerId peerId, NetworkConnection? connection, int hops)
        {
            Peer = peerId;
            if (connection != null)
            {
                ArgumentOutOfRangeException.ThrowIfZero(hops, nameof(hops));
                Connection = new WeakReference<NetworkConnection>(connection);
            }
            else
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThan(hops, 0, nameof(hops));
                Connection = null;
            }
            Hops = hops;
        }

        public PeerId Peer { get; }


        /// <summary>
        /// Gets or sets the connection associated with the route.
        /// </summary>
        public WeakReference<NetworkConnection>? Connection { get; }


        public int Hops { get; }
    }

}
