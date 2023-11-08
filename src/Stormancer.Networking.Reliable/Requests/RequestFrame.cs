using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable.Requests
{
    /*
        Request frame header
        | IsRqFrame | HasDest    | IsOutput |Is Start|IsComplete| IsError  |Is Cancel|Rqid length|
        |-----------|------------|----------|--------|----------|----------|---------|-----------|    
        |     1     |     1      |     1    |    1   |    1     |     1    |    1    |     1     |
        |----------------------------------------------------------------------------------------|
        |                              Request Id (8/16 bit)                                     |
        |----------------------------------------------------------------------------------------|
        |                                     Peer Id                                            |
        |                                      64bit                                             |
        |----------------------------------------------------------------------------------------|
        |                                 Content length (16bit)                                 |
        |----------------------------------------------------------------------------------------|
        |                                 Optional OperationId                                   |
        |----------------------------------------------------------------------------------------|
        |                                 Optional Payload                                       |
        |----------------------------------------------------------------------------------------|
        
    If 'IsStart' flag set, Content starts with the OperationId, following by the frame payload.

    OperationId:
        |   OpId length |
        |---------------|
        |       8       |
        |---------------|
        |     Op Id     |
        | Up to 256bytes|
    */


    internal enum RequestFrameFlags : byte
    {
        IsRequest = 0b10000000,
        HasDest = 0b01000000,
        IsOutput = 0b00100000,
        IsStart = 0b00010000,
        IsComplete = 0b00001000,
        IsError = 0b00000100,
        IsCancel = 0b00000010,
        RqId16bit = 0b00000001
    }
    internal enum RequestFrameIdLength
    {
        Byte,
        Short,
    }

    internal class RequestFrame
    {

        public void Reset()
        {
            Destination = default;
            RequestId = default;
            Flags = default;
            OperationId = ReadOnlySequence<byte>.Empty;
            Payload = ReadOnlySequence<byte>.Empty;



        }

        public bool IsRequestFrame => (Flags & RequestFrameFlags.IsRequest) != 0;
        public bool IsRequestStartFrame => (Flags & RequestFrameFlags.IsStart) != 0;
        public bool IsRequestCompleteFrame => (Flags & RequestFrameFlags.IsComplete) != 0;
        public bool IsRequestErrorFrame => (Flags & RequestFrameFlags.IsError) != 0;
        public bool IsRequestCancelFrame => (Flags & RequestFrameFlags.IsCancel) != 0;

        public RequestFrameFlags Flags { get; set; }

        ushort RequestId { get; set; }
        public PeerId Destination { get; set; }

        public ReadOnlySequence<byte> OperationId { get; set; }
        public ReadOnlySequence<byte> Payload { get; set; }



        private const int MinHeaderLength = 4;

        public bool TryReadFrame(ref ReadOnlySequence<byte> buffer)
        {
            Reset();
            var bufferLength = buffer.Length;
            if (bufferLength < MinHeaderLength)
            {
                return false;
            }




            var reader = new SequenceReader<byte>(buffer);
            if (!reader.TryRead(out byte flags))
            {
                return false;
            }
            Flags = (RequestFrameFlags)flags;


            if ((Flags & RequestFrameFlags.RqId16bit) == 0)
            {
                if (!reader.TryRead(out byte length))
                {
                    return false;
                }
                else
                {
                    RequestId = (ushort)length;
                }

            }
            else
            {
                if (!reader.TryReadBigEndian(out short length))
                {
                    return false;
                }
                else
                {
                    RequestId = (ushort)length;
                }
            }


            if ((Flags & RequestFrameFlags.HasDest) != 0)
            {
                Span<byte> idSpan = stackalloc byte[16];
                if (reader.TryCopyTo(idSpan))
                {
                    this.Destination = new PeerId(new Guid(idSpan));
                    reader.Advance(16);
                }
                else
                {
                    return false;
                }
            }

            if (!reader.TryReadBigEndian(out short contentLengthRead))
            {
                return false;
            }
            ushort contentLength = (ushort)contentLengthRead;
            ushort payloadLength = contentLength;
            //If IsStart True, the content starts with an operation id.
            if (IsRequestStartFrame)
            {
                if (!reader.TryRead(out var operationIdLength))
                {
                    return false;
                }

                if (operationIdLength+1 > contentLength)
                {
                    throw new InvalidOperationException("Invalid data");
                }

                OperationId = reader.UnreadSequence.Slice(0,operationIdLength);
                reader.Advance(operationIdLength);
                payloadLength = (ushort)(contentLength - operationIdLength -1);
            }

            Payload = reader.UnreadSequence.Slice(0,payloadLength);

            buffer = buffer.Slice(Payload.End);
            return true;
        }


    }
}
