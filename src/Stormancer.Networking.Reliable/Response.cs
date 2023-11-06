using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    /// <summary>
    /// A response to a request.
    /// </summary>
    public class Response : IDisposable
    {
        internal Response(PeerId origin, PipeReader content)
        {
            Origin = origin;
            Content = content;
        }

        /// <summary>
        /// Gets the <see cref="PeerId"/> that represents the origin of the response.
        /// </summary>
        public PeerId Origin { get; }


        /// <summary>
        /// Gets a <see cref="PipeReader"/> instance to read the content of the response.
        /// </summary>
        public PipeReader Content { get; }

        /// <summary>
        /// Disposes the object.
        /// </summary>
        public void Dispose()
        {
            Content.Complete(new OperationCanceledException());
        }
    }
}
