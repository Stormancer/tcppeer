using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable.Tcp
{
    internal static partial class SocketLogs
    {

        [LoggerMessage(0, LogLevel.Debug, @"Connection ""{connectionId}"" created. Local:""{localEndpoint}"" Remote:""{remoteEndpoint}"" ")]
        public static partial void ConnectionCreated(ILogger logger, string connectionId,EndPoint localEndpoint, EndPoint remoteEndpoint);
        
        [LoggerMessage(1, LogLevel.Debug, @"Connection ""{connectionId}"" reset.")]
        public static partial void ConnectionReset(ILogger logger, string connectionId);

        [LoggerMessage(2, LogLevel.Error, "Failed to initialize a network connection with {remoteEndPoint}.")]
        public static partial void FailedCreatingNetworkConnection(ILogger logger,EndPoint? remoteEndPoint, Exception ex);


        [LoggerMessage(3, LogLevel.Debug, @"Connection id ""{ConnectionId}"" sending RST because: ""{Reason}""", EventName = "ConnectionWriteRst")]
        public static partial void ConnectionWriteRst(ILogger logger, string connectionId, string reason);

        [LoggerMessage(4, LogLevel.Debug, @"Connection id ""{ConnectionId}"" sending FIN because: ""{Reason}""", EventName = "ConnectionWriteFin")]
        public static partial void ConnectionWriteFin(ILogger logger, string connectionId, string reason);

        [LoggerMessage(5, LogLevel.Debug, @"Connection id ""{ConnectionId}"" received FIN.", EventName = "ConnectionReadFin")]
        public static partial void ConnectionReadFin(ILogger logger, string connectionId);

        [LoggerMessage(6, LogLevel.Debug, @"Connection id ""{ConnectionId}"" paused.", EventName = "ConnectionPause", SkipEnabledCheck = true)]
        public static partial void ConnectionPause(ILogger logger, string connectionId);

        [LoggerMessage(7, LogLevel.Debug, @"Connection id ""{ConnectionId}"" resumed.", EventName = "ConnectionResume", SkipEnabledCheck = true)]
        public static partial void ConnectionResume(ILogger logger, string connectionId);

        [LoggerMessage(8, LogLevel.Debug, @"Connection id ""{ConnectionId}"" communication error.", EventName = "ConnectionError", SkipEnabledCheck = true)]
        public static partial void ConnectionError(ILogger logger, string connectionId, Exception ex);



    }
}
