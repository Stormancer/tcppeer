using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    internal class PipeReaderWrapper : PipeReader, IAsyncDisposable
    {
        private PipeReader? _internalReader;

        public PipeReaderWrapper(PipeReader internalReader)
        {
            _internalReader = internalReader;
        }
        public override void AdvanceTo(SequencePosition consumed)
        {
            if (_internalReader != null)
            {
                _internalReader.AdvanceTo(consumed);
            }
            else
            {
                throw new ObjectDisposedException("RequestContext");
            }
        }

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            if (_internalReader != null)
            {
                _internalReader.AdvanceTo(consumed, examined);
            }
            else
            {
                throw new ObjectDisposedException("RequestContext");
            }
        }

        public override void CancelPendingRead()
        {
            if (_internalReader != null)
            {
                _internalReader.CancelPendingRead();
            }
            else
            {
                throw new ObjectDisposedException("RequestContext");
            }
        }

        public override void Complete(Exception? exception = null)
        {
            if (_internalReader != null)
            {
                _internalReader.Complete(exception);
            }
            else
            {
                throw new ObjectDisposedException("RequestContext");
            }
        }

        public ValueTask DisposeAsync()
        {
            var reader = _internalReader;
            _internalReader = null;
            if (reader != null)
            {
                return reader.CompleteAsync();
            }
            else
            {
                return ValueTask.CompletedTask;
            }

        }

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (_internalReader != null)
            {
                return _internalReader.ReadAsync(cancellationToken);

            }
            else
            {
                throw new ObjectDisposedException("RequestContext");
            }
        }

        public override bool TryRead(out ReadResult result)
        {
            if (_internalReader != null)
            {
                return _internalReader.TryRead(out result);
            }
            else
            {
                throw new ObjectDisposedException("RequestContext");
            }
        }
    }

    internal class PipeWriterWrapper : PipeWriter, IAsyncDisposable
    {
        private PipeWriter? _internalWriter;

        public PipeWriterWrapper(PipeWriter internalWriter)
        {
            _internalWriter = internalWriter;
        }
        public override void Advance(int bytes)
        {
            if (_internalWriter != null)
            {
                _internalWriter.Advance(bytes);
            }
            else
            {
                throw new ObjectDisposedException("RequestContext");
            }
        }

        public override void CancelPendingFlush()
        {
            if (_internalWriter != null)
            {
                _internalWriter.CancelPendingFlush();
            }
            else
            {
                throw new ObjectDisposedException("RequestContext");
            }
        }

        public override void Complete(Exception? exception = null)
        {
            if (_internalWriter != null)
            {
                _internalWriter.Complete(exception);
            }
            else
            {
                throw new ObjectDisposedException("RequestContext");
            }
        }

        public ValueTask DisposeAsync()
        {
            var writer = _internalWriter;
            _internalWriter= null;
            if (writer != null)
            {
                return writer.CompleteAsync();
            }
            else
            {
                return ValueTask.CompletedTask;
            }
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            if (_internalWriter != null)
            {
                return _internalWriter.FlushAsync(cancellationToken);
            }
            else
            {
                throw new ObjectDisposedException("RequestContext");
            }
        }

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (_internalWriter != null)
            {
                return _internalWriter.GetMemory(sizeHint);
            }
            else
            {
                throw new ObjectDisposedException("RequestContext");
            }
        }

        public override Span<byte> GetSpan(int sizeHint = 0)
        {
            if (_internalWriter != null)
            {
                return _internalWriter.GetSpan(sizeHint);
            }
            else
            {
                throw new ObjectDisposedException("RequestContext");
            }
        }
    }
}
