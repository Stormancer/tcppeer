using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    internal class RequestProcessor
    {
        private readonly HandlerStore _handlers;
        private readonly ILogger _logger;

        public RequestProcessor(HandlerStore handlers, ILogger logger)
        {
            _handlers = handlers;
            _logger = logger;
        }



        public ValueTask RunRequestHandlerAsync(OperationId operation, RequestId requestId, IReadOnlyDictionary<string, string> headers, PipeReader contentReader, PipeWriter contentWriter, CancellationToken cancellationToken)
        {
            if (_handlers.TryGet(new ReadOnlySequence<byte>(operation.Buffer), out var handler))
            {
                return RunRequestHandlerAsync(operation, requestId, headers, contentReader, contentWriter, handler, cancellationToken);
            }
            else
            {
                return contentReader.CompleteAsync(new OperationNotFoundException(operation));
            }
        }





        public ValueTask RunRequestHandlerAsync(OperationId operation,RequestId requestId, IReadOnlyDictionary<string, string> headers, PipeReader contentReader, PipeWriter contentWriter, OperationHandler handler, CancellationToken cancellationToken)
        {
            var ctx = new RequestContext(operation,requestId, headers, contentReader, contentWriter);

           
            ValueTask task;
            try
            {
                task = handler.Handler(ctx);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing a remote operation '{operationId}'", operation);
                
                
                return ctx.CompleteWithErrorAsync(ex);

            }

            if (task.IsCompletedSuccessfully)
            {
                return ctx.DisposeAsync();
            }
            else if (task.IsFaulted)
            {
                var ex = task.AsTask().Exception;
                _logger.LogError(ex, "An error occurred while processing a remote operation '{operationId}'", operation);
                return ctx.CompleteWithErrorAsync(ex);
               
            }
            else
            {
                async static ValueTask RunRequestHandlerAsyncInternal(ValueTask task, RequestContext ctx, ILogger logger)
                {
                    await using (ctx)
                    {
                        try
                        {

                            await task;

                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "An error occurred while processing a remote operation '{operationId}'",  ctx.Operation);
                            await ctx.CompleteWithErrorAsync(ex);
                        }
                    }
                }

                return RunRequestHandlerAsyncInternal(task, ctx, _logger);

            }


        }
    }
}
