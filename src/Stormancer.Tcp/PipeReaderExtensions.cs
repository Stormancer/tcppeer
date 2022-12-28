using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Tcp
{
    internal static class PipeReaderExtensions
    {
        public static async ValueTask CopyToAsync(this PipeReader reader, PipeWriter?[] writers, CancellationToken ct = default, bool closeOnEnd = false)
        {
            ReadResult result;
            do
            {
                result = await reader.ReadAsync(ct);
                var buffer = result.Buffer;

                for (int i = 0; i < writers.Length; i++)
                {
                    var writer = writers[i];
                    if (writer != null)
                    {
                        try
                        {
                            var mem = writer.GetMemory((int)buffer.Length);

                            foreach(var segment in buffer)
                            {
                                segment.Span.CopyTo(mem.Span);
                            }
                            writer.Advance((int)buffer.Length);
                            await writer.FlushAsync(ct);
                        }
                        catch
                        {
                            writers[i] = null;
                        }
                    }
                }
                reader.AdvanceTo(buffer.End, buffer.End);
            }
            while (!result.IsCompleted && !result.IsCanceled);

            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }

            if (closeOnEnd)
            {
                for (int i = 0; i < writers.Length; i++)
                {
                    var writer = writers[i];
                    if (writer is not null)
                    {

                        await writer.FlushAsync();
                        writer.Complete();
                    }

                }
            }
        }
    }
}
