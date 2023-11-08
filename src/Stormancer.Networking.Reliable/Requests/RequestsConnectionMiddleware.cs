using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable.Requests
{
    internal static class RequestsConnectionExtensions
    {
        public static TransportsOptions UseRequestServer(this TransportsOptions options,ServiceContext serviceContext)
        {
            var middleware = new RequestsConnectionMiddleware(serviceContext);
            return options.Use(next => middleware.OnConnectionAsync);
            
        }
    }

    internal class RequestsConnectionMiddleware
    {
        private readonly ServiceContext _serviceContext;

        public RequestsConnectionMiddleware(ServiceContext serviceContext)
        {
            _serviceContext = serviceContext;
        }

        internal Task OnConnectionAsync(NetworkConnection netConnection)
        {

            var ctx = new RequestConnectionContext(netConnection, _serviceContext);
            var connection = new RequestConnection(ctx);

            return connection.ProcessRequestsAsync();
        }
    }
}
