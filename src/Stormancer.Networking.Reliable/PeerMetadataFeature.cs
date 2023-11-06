using Microsoft.AspNetCore.Connections;
using Stormancer.Networking.Reliable.Features;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    internal static class ConnectionReadyExtensions
    {
        public static TransportsOptions UseMetadataExchange(this TransportsOptions options )
        {

            options.Use(next=> new PeerMetadataMiddleware(next).OnConnectionAsync);
            return options;
        }
    }
    internal class PeerMetadataMiddleware(ConnectionDelegate next)
    {
        private readonly ConnectionDelegate _next = next;

        public async Task OnConnectionAsync(NetworkConnection connection)
        {
            var feature = connection.Features.Get<IPeerConnectionFeature>() as PeerConnectionFeature;
            Debug.Assert(feature?.LocalPeerMetadata != null);

            PeerMetadata.Write(connection.Transport.Output, feature.LocalPeerMetadata);
            await connection.Transport.Output.FlushAsync();

            feature.RemotePeerMetadata = await PeerMetadata.ReadMetadataAsync(connection.Transport.Input, CancellationToken.None);

            feature.SetMetadataExchanged();
            await _next(connection);
        }
    }
    
}
