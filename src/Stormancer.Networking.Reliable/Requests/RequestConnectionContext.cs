using Stormancer.Networking.Reliable.Features;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable.Requests
{
    internal class RequestConnectionContext
    {
        public RequestConnectionContext(NetworkConnection connection, ServiceContext serviceContext)
        {
            ConnectionFeatures = connection.Features;
            ServiceContext = serviceContext;
            Connection = connection;
            ConnectionId = connection.ConnectionId;

            var memoryPoolFeature = connection.Features.Get<IMemoryPoolFeature>();
            MemoryPool = memoryPoolFeature?.MemoryPool ?? MemoryPool<byte>.Shared;

            var peerConnectionFeature = ConnectionFeatures.Get<IPeerConnectionFeature>();

            Debug.Assert(peerConnectionFeature?.RemotePeerMetadata != null);
            Debug.Assert(peerConnectionFeature?.LocalPeerMetadata != null);
            RemotePeerMetadata = peerConnectionFeature.RemotePeerMetadata;
            LocalPeerMetadata = peerConnectionFeature.LocalPeerMetadata;

        }
        public string ConnectionId { get; }
        public IFeatureCollection ConnectionFeatures { get; }

        public PeerMetadata RemotePeerMetadata { get; }
        public PeerMetadata LocalPeerMetadata { get; }
        public MemoryPool<byte> MemoryPool { get; }

        public ServiceContext ServiceContext { get; }

        public NetworkConnection Connection { get; }
    }
}
