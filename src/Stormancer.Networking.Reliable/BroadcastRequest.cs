using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    /// <summary>
    ///  A broadcast request to all peers in the mesh.
    /// </summary>
    public class BroadcastRequest
    {
        /// <summary>
        /// Starts sending request content and receiving response data from remote peers.
        /// </summary>
        public void Start()
        {
        }


        /// <summary>
        /// Gets a <see cref="PipeWriter"/> used to write data sent to all peers in the mesh.
        /// </summary>
        public PipeWriter Content { get; }

        /// <summary>
        /// Gets the responses to the request.
        /// </summary>
        public IAsyncEnumerable<Response> Responses { get; }
    }


}
