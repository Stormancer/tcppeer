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
    /// <summary>
    /// Metadata associated with a peer.
    /// </summary>
    public class PeerMetadata : IDisposable
    {
        internal static void Write(IBufferWriter<byte> writer, PeerMetadata metadata)
        {
            var mem = writer.GetMemory(18 + metadata.Content.Length);
            metadata.PeerId.Value.TryWriteBytes(mem.Span);
            BinaryPrimitives.TryWriteUInt16BigEndian(mem.Slice(16).Span, (ushort)metadata.Content.Length);
            metadata.Content.CopyTo(mem.Slice(18));

            writer.Advance(18 + metadata.Content.Length);
        }

        internal static bool TryRead(ReadOnlySequence<byte> buffer, [NotNullWhen(true)] out PeerMetadata? metadata, out int consumed)
        {
            if (buffer.Length < 18)
            {

                metadata = default;
                consumed = 18;
                return false;
            }

            var reader = new SequenceReader<byte>(buffer);
            reader.Advance(16);
            reader.TryReadBigEndian(out short lengthSigned);
            var length = (ushort)lengthSigned;

            if (buffer.Length < 18 + length)
            {

                metadata = default;
                consumed = 18 + length;
                return false;
            }

            Span<byte> idSpan = stackalloc byte[16];

            reader.Rewind(18);
            reader.TryCopyTo(idSpan);

            var metadataOwner = MemoryPool<byte>.Shared.Rent(length);
            buffer.Slice(18, length).CopyTo(metadataOwner.Memory.Span);

            metadata = new PeerMetadata(new PeerId(new Guid(idSpan)), metadataOwner, metadataOwner.Memory.Slice(0, length));
            consumed = 18 + length;
            return true;
        }

        internal static async ValueTask<PeerMetadata> ReadMetadataAsync(PipeReader reader, CancellationToken cancellationToken)
        {
            do
            {
                var result = await reader.ReadAtLeastAsync(18, cancellationToken);

                if (result.Buffer.Length < 18 | result.IsCanceled)
                {
                    reader.AdvanceTo(result.Buffer.Start);
                    throw new OperationCanceledException();
                }

                if (TryRead(result.Buffer, out var metadata, out var consumed))
                {
                    reader.AdvanceTo(result.Buffer.GetPosition(consumed));

                    return metadata;
                }
                else
                {
                    reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);

                    if (result.IsCompleted)
                    {
                        IncompleteMessageException.ThrowException(result.Buffer.Length, consumed);
                    }
                }


            }
            while (!cancellationToken.IsCancellationRequested);

            throw new OperationCanceledException();
        }

        internal PeerMetadata(PeerId peerId, IMemoryOwner<byte> owner, ReadOnlyMemory<byte> metadata)
        {
            PeerId = peerId;
            _owner = owner;
            Content = metadata;
        }

        internal PeerMetadata()
        {
            PeerId = new PeerId();
            Content = ReadOnlyMemory<byte>.Empty;
            _owner = null;
        }
        private readonly IMemoryOwner<byte>? _owner;

        /// <summary>
        /// Gets the unique id of the peer.
        /// </summary>
        public PeerId PeerId { get; }
        public ReadOnlyMemory<byte> Content { get; }

        /// <summary>
        /// Disposes the metadata object and all associated memory.
        /// </summary>
        public void Dispose()
        {
            _owner?.Dispose();
        }





    }
}
