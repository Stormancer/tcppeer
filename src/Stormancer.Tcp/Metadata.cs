using System;
using System.Buffers.Binary;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO.Pipelines;

namespace Stormancer.Tcp
{

    public struct Metadata : IDisposable
    {
        internal static void Write(IBufferWriter<byte> writer, Metadata metadata)
        {
            var mem = writer.GetMemory(18 + metadata.Buffer.Length);
            metadata.PeerId.TryWriteBytes(mem.Span);
            BinaryPrimitives.TryWriteUInt16BigEndian(mem.Slice(16).Span, (ushort)metadata.Buffer.Length);
            metadata.Buffer.CopyTo(mem.Slice(18));

            writer.Advance(18 + metadata.Buffer.Length);
        }

        internal static bool TryRead(ReadOnlySequence<byte> buffer, out Metadata metadata, out int consumed)
        {
            if (buffer.Length < 18)
            {
               
                metadata = default;
                consumed = 0;
                return false;
            }

            var reader = new SequenceReader<byte>(buffer);
            reader.Advance(16);
            reader.TryReadBigEndian(out short lengthSigned);
            var length = (ushort)lengthSigned;

            if (buffer.Length < 18 + length)
            {
              
                metadata = default;
                consumed = 0;
                return false;
            }

            Span<byte> idSpan = stackalloc byte[16];

            reader.Rewind(18);
            reader.TryCopyTo(idSpan);
          
            var metadataOwner = MemoryPool<byte>.Shared.Rent(length);
            buffer.Slice(18, length).CopyTo(metadataOwner.Memory.Span);
           
            metadata = new Metadata(new Guid(idSpan), metadataOwner, metadataOwner.Memory.Slice(0, length));
            consumed = 18+length;
            return true;
        }

        internal static async ValueTask<Metadata> ReadMetadataAsync(PipeReader reader, CancellationToken cancellationToken)
        {
            do
            {
                var result = await reader.ReadAtLeastAsync(18,cancellationToken);

                if(result.Buffer.Length < 18 | result.IsCanceled)
                {
                    reader.AdvanceTo(result.Buffer.Start);
                    throw new OperationCanceledException();
                }

                if(TryRead(result.Buffer,out var metadata, out var consumed))
                {
                    reader.AdvanceTo(result.Buffer.GetPosition(consumed));

                    return metadata;
                }
                else
                {
                    reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
                }
            }
            while (!cancellationToken.IsCancellationRequested);

            throw new OperationCanceledException();
        }

        internal Metadata(Guid peerId, IMemoryOwner<byte> owner, ReadOnlyMemory<byte> metadata)
        {
            PeerId = peerId;
            _owner = owner;
            Buffer = metadata;
        }

        public Metadata()
        {
            PeerId = Guid.NewGuid();
            Buffer = ReadOnlyMemory<byte>.Empty;
            _owner = null;
        }
        private readonly IMemoryOwner<byte>? _owner;

        public Guid PeerId { get; }
        public ReadOnlyMemory<byte> Buffer { get; }

        public void Dispose()
        {
            _owner?.Dispose();
        }





    }
}
