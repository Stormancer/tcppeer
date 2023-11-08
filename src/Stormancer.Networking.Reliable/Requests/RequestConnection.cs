using Microsoft.Extensions.Logging;
using Stormancer.Networking.Reliable.Features;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable.Requests
{
    internal class RequestConnection
    {
        private static class GracefulCloseInitiator
        {
            public const int None = 0;
            public const int Server = 1;
            public const int Client = 2;
        }
        public RequestConnection(RequestConnectionContext ctx)
        {
            _ctx = ctx;
            _logger = _ctx.ServiceContext.LoggerFactory.CreateLogger<RequestConnection>();
            _frameWriter = new RequestFrameWriter(_ctx);
            ctx.ConnectionFeatures.Set(_frameWriter);
        }

        private readonly RequestConnectionContext _ctx;
        private readonly RequestFrameWriter _frameWriter;

        private readonly ILogger _logger;
        private int _gracefulCloseInitiator;
        private bool _gracefulCloseStarted;
        private int _isClosed = 0;

        public PipeReader Input => _ctx.Connection.Transport.Input;
        internal async Task ProcessRequestsAsync()
        {
            var connectionLifetimeNotificationFeature = _ctx.ConnectionFeatures.Get<IConnectionLifetimeNotificationFeature>();

            
            //Graceful termination of the server/connection.
            using var shutdownRegistration =  connectionLifetimeNotificationFeature?.ConnectionClosedRequested.Register(state=>((RequestConnection)state!).StopProcessing(true),this);

            //Connection lost
            using var closeRegistration = _ctx.Connection.ConnectionClosed.Register(state => ((RequestConnection)state!).OnConnectionClosed(), this);


            try
            {
                var frame = new RequestFrame();
                var peerConnectionFeature = (PeerConnectionFeature?)_ctx.ConnectionFeatures.Get<IPeerConnectionFeature>();
                peerConnectionFeature?.SetReadyToProcessData();
                while(_isClosed == 0)
                {
                    var result = await Input.ReadAsync();
                    var buffer = result.Buffer;

                    try
                    {
                        bool frameReceived = false;
                        while(frame.TryReadFrame(ref buffer))
                        {
                            frameReceived = true;
                            ProcessFrame(frame);
                        }

                        if (result.IsCompleted)
                        {
                            return;
                        }
                    }
                    finally
                    {
                        Input.AdvanceTo(buffer.Start, buffer.End);
                        UpdateConnectionState();
                    }
                   
                }

                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection processing ended abnormally.");
            }
        }

        private void ProcessFrame(RequestFrame frame)
        {
            _ctx.ServiceContext.RequestFrameProcessor.ProcessFrame(frame);
        }

        private void StopProcessing(bool serverInitiated)
        {
            var initiator = serverInitiated ? GracefulCloseInitiator.Server : GracefulCloseInitiator.Client;

            if (Interlocked.CompareExchange(ref _gracefulCloseInitiator, initiator, GracefulCloseInitiator.None) == GracefulCloseInitiator.None)
            {
                Input.CancelPendingRead();
            }
        }

        private void OnConnectionClosed()
        {

        }


        private void UpdateConnectionState()
        {
            if (_isClosed != 0)
            {
                return;
            }

            if (_gracefulCloseInitiator != GracefulCloseInitiator.None && !_gracefulCloseStarted)
            {
                _gracefulCloseStarted = true;



                if (TryClose())
                {
                    _frameWriter.WriteGoAwayAsync("ok").Preserve();
                }
            }

        }

        private bool TryClose()
        {
            if (Interlocked.Exchange(ref _isClosed, 1) == 0)
            {
                return true;
            }

            return false;
        }
    }
}
