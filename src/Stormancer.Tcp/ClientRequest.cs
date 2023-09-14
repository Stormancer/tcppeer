using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Tcp
{
    /// <summary>
    /// A network request to a peer.
    /// </summary>
    public class PeerRequest : IDisposable
    {
        private readonly NetworkClient _peer;

        private readonly Pipe _contentPipe;
        private readonly Pipe _responsePipe;

        internal PeerRequest(NetworkClient peer, PeerId destination, string operation, PipeOptions pipeOptions)
        {
            _peer = peer;
            _contentPipe = new Pipe(pipeOptions); 
            _responsePipe = new Pipe(pipeOptions);
            Operation = operation;

            Response = new Response(destination, _responsePipe.Reader);
        }
        /// <summary>
        /// Starts sending the request and receiving response data.
        /// </summary>
        public void Start()
        {
            _peer.StartRequest(this,_contentPipe,_responsePipe);
        }

        /// <summary>
        /// Gets the name of the operation.
        /// </summary>
        public string Operation { get; }

        /// <summary>
        /// Disposes the request.
        /// </summary>
        public void Dispose()
        {
            Response.Dispose();
            Content.Complete(new OperationCanceledException());
        }

        /// <summary>
        /// Gets the <see cref="Tcp.Response"/> of the request.
        /// </summary>
        public Response Response { get; }

        /// <summary>
        /// Gets the <see cref="PipeWriter"/> instance used to write the content of the request.
        /// </summary>
        public PipeWriter Content => _contentPipe.Writer;
    }
}
