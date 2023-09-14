using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Tcp
{
    /// <summary>
    /// 
    /// </summary>
    public class NetworkClient : IAsyncDisposable
    {
        private readonly PeerOptions _configuration;
        private readonly PipeOptions _pipeOptions;

        private readonly Counters _counters;
        public NetworkClient(PeerOptions configuration)
        {
            _configuration = configuration;
            _pipeOptions = new PipeOptions(_configuration.MemoryPool );
            _counters = new Counters(_configuration.Name);
        }

        /// <summary>
        /// Creates a request without sending it immediately.
        /// </summary>
        /// <param name="destination"></param>
        /// <param name="operation"></param>
        /// <returns></returns>
        public PeerRequest CreateRequest(PeerId destination, string operation)
        {
            var request = new PeerRequest(this, destination, operation, _pipeOptions);

            return request;
        }

        /// <summary>
        /// Sends a request to the target peer immediately.
        /// </summary>
        /// <param name="destination"></param>
        /// <param name="operation"></param>
        /// <returns></returns>
        public PeerRequest SendRequest(PeerId destination, string operation)
        {
            var rq = CreateRequest(destination, operation);

            rq.Start();
            return rq;
        }

        public BroadcastRequest Broadcast(string operation)
        {
            var rq = CreateBroadcastRequest(operation);

            rq.Start();
            return rq;
        }
        public BroadcastRequest CreateBroadcastRequest(string operation)
        {
            return new BroadcastRequest();
        }

        public ValueTask DisposeAsync()
        {
            throw new NotImplementedException();
        }



        internal void StartRequest(PeerRequest peerRequest, Pipe contentPipe, Pipe responsePipe)
        {
            _counters.Sent.Add(1);
        }
    }
}
