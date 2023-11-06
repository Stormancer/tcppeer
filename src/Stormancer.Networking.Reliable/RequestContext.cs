using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    /// <summary>
    /// Object passed as argument to request handlers.
    /// </summary>
    public class RequestContext : IAsyncDisposable
    {
        

        private readonly PipeWriterWrapper _writer;
        private readonly PipeReaderWrapper _reader;

        internal RequestContext(OperationId operation, RequestId requestId, IReadOnlyDictionary<string,string> headers, PipeReader reader, PipeWriter writer)
        {
            Operation = operation;
            RequestId = requestId;
            Headers = headers;
            _writer = new PipeWriterWrapper(writer);
            _reader = new PipeReaderWrapper(reader); 
        }

        /// <summary>
        /// Gets the id of the current operation.
        /// </summary>
        public OperationId Operation { get; }

        /// <summary>
        /// Gets the id of the request.
        /// </summary>
        public RequestId RequestId { get; }


        /// <summary>
        /// Gets the header
        /// </summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>
        /// Gets a <see cref="PipeReader"/> used to read the content of the request.
        /// </summary>
        public PipeReader Content => _reader;

        /// <summary>
        /// Gets a <see cref="PipeWriter"/> used to write the response to the request.
        /// </summary>
        public PipeWriter Response => _writer;

        /// <summary>
        /// Completes the request processing.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            var t1 = _reader.DisposeAsync();
            var t2 = _writer.DisposeAsync();

            if(t1.IsCompleted && t2.IsCompleted)
            {
                return ValueTask.CompletedTask;
            }
            else
            {
                async ValueTask WhenAll(ValueTask t1,ValueTask t2) 
                {
                    await t1;
                    await t2;
                }
                return WhenAll(t1, t2);
            }
        }

        internal ValueTask CompleteWithErrorAsync(Exception ex)
        {
            var t1 =_reader.CompleteAsync(ex);
            var t2 = _writer.CompleteAsync(ex);

            if (t1.IsCompleted && t2.IsCompleted)
            {
                return ValueTask.CompletedTask;
            }
            else
            {
                async ValueTask WhenAll(ValueTask t1, ValueTask t2)
                {
                    await t1;
                    await t2;
                }
                return WhenAll(t1, t2);
            }
        }
    }
}
