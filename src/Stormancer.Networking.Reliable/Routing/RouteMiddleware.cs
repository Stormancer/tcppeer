using Stormancer.Networking.Reliable.Features;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable.Routing
{

    internal static class RouteTableMiddlewareExtensions
    {
        public static TransportsOptions UseRouteTableMiddleware(this TransportsOptions options, RouteTable table)
        {
            options.Use(next => new RouteTableMiddleware(next, table).ConnectAsync);
            return options;
        }
    }
    internal class RouteTableMiddleware(ConnectionDelegate next, RouteTable routeTable)
    {
        private readonly ConnectionDelegate _next = next;
        private readonly RouteTable _routeTable = routeTable;

        public async Task ConnectAsync(NetworkConnection connection)
        {
            try
            {
                var feature = connection.Features.Get<IPeerConnectionFeature>();
                Debug.Assert(feature?.RemotePeerMetadata != null);

                _routeTable.AddRoute(feature.RemotePeerMetadata.PeerId, connection, 1);
                await _next(connection);
            }
            finally
            {
                _routeTable.RemoveRoutes(connection);
            }
        }
    }
}
