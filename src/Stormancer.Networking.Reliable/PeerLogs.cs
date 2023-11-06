using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    internal static partial class PeerLogs
    {
        [LoggerMessage(1, LogLevel.Critical, @"Connection id ""{ConnectionId}"" application never completed.", EventName = "ApplicationNeverCompleted")]
        public static partial void ApplicationNeverCompleted(ILogger logger, string connectionId);

        [LoggerMessage(2, LogLevel.Debug, @"Connection id ""{ConnectionId}"" accepted.", EventName = "ConnectionAccepted")]
        internal static partial void ConnectionAccepted(ILogger logger, string connectionId);

        [LoggerMessage(3, LogLevel.Debug, @"Connection id ""{ConnectionId}"" started.", EventName = "ConnectionStart")]
        public static partial void ConnectionStart(ILogger logger, string connectionId);

        [LoggerMessage(4, LogLevel.Debug, @"Connection id ""{ConnectionId}"" stopped.", EventName = "ConnectionStop")]
        public static partial void ConnectionStop(ILogger logger, string connectionId);

        [LoggerMessage(5, LogLevel.Debug, "Some connections failed to close gracefully during server shutdown.", EventName = "NotAllConnectionsClosedGracefully")]
        public static partial void NotAllConnectionsClosedGracefully(ILogger logger);

        [LoggerMessage(6, LogLevel.Debug, "Some connections failed to abort during server shutdown.", EventName = "NotAllConnectionsAborted")]
        public static partial void NotAllConnectionsAborted(ILogger logger);

    }
}
