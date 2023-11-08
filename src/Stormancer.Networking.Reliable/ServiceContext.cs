using Microsoft.Extensions.Logging;
using Stormancer.Networking.Reliable.Requests;
using Stormancer.Networking.Reliable.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    internal class ServiceContext(
        ILoggerFactory loggerFactory,
        IRequestFrameProcessor processor,
        ConnectionManager connectionManager,
        TransportManager transportManager)
    {
        public ServiceContext(ILoggerFactory loggerFactory, IRequestFrameProcessor processor, ConnectionManager connectionManager, TransportManager transportManager, RouteTable routeTable) : this(loggerFactory, processor, connectionManager, transportManager)
        {
            RouteTable = routeTable;
        }

        public ILoggerFactory LoggerFactory { get; } = loggerFactory;
        public IRequestFrameProcessor RequestFrameProcessor { get; } = processor;
        public ConnectionManager ConnectionManager { get; } = connectionManager;
        public TransportManager TransportManager { get; } = transportManager;
        public RouteTable RouteTable { get; }
    }
}
