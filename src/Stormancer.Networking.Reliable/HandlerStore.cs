using CommunityToolkit.HighPerformance;
using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
   /// <summary>
   /// Id of an operation which can be performed on a peer.
   /// </summary>
    public class OperationId : IEquatable<OperationId>, IEquatable<ReadOnlySequence<byte>>
    {
        /// <summary>
        /// Creates a new <see cref="OperationId"/> object.
        /// </summary>
        /// <param name="operationId"></param>
        public OperationId(string operationId)
        {
            var byteCount = Encoding.UTF8.GetByteCount(operationId);
            var data = new byte[1 + byteCount];
            data[0] = (byte)byteCount;
            Encoding.UTF8.GetBytes(operationId, data.AsSpan().Slice(1));
            Buffer = new ReadOnlyMemory<byte>(data);
            Name = operationId;
        }

      
        /// <summary>
        /// Gets a buffer in the memory storing a binary version of the <see cref="OperationId"/>
        /// </summary>
        public ReadOnlyMemory<byte> Buffer { get; }

        /// <summary>
        /// Gets an hash of the operation id.
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return Buffer.Span.GetDjb2HashCode();
        }

        /// <summary>
        /// Compares the <see cref="OperationId"/> with another object for equality.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if(obj is OperationId other)
            {
                return Equals(other);
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Compares the <see cref="OperationId"/> with another <see cref="OperationId"/> for equality.
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Equals(OperationId? other)
        {
            if (other is not null)
            {
                return other.Buffer.Span.SequenceEqual(Buffer.Span);
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Compares the <see cref="OperationId"/> with a binary representation.
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Equals(ReadOnlySequence<byte> other)
        {
            if(other.Length != Buffer.Length)
            {
                return false;
            }

            if(other.IsSingleSegment)
            {
                return Buffer.Span.SequenceEqual(other.FirstSpan);
            }
            else
            {
                Span<byte> span = stackalloc byte[(int)other.Length];
                other.CopyTo(span);
                return Buffer.Span.SequenceEqual(span);
            }
        }
        /// <summary>
        /// Gets the name of the operation.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets a <see cref="string"/> representation of the operation.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return Name;
        }
    }

    internal class HandlerStore
    {
        public HandlerStore(Func<IEnumerable<IPeerApiConfigurator>> configurators)
        {
            this.configurators = configurators;
        }
            
       
     
        public OperationHandler[]? Handlers { get; private set; }

        private bool _initialized = false;
        private readonly Func<IEnumerable<IPeerApiConfigurator>> configurators;
      
        public void Initialize(PeerMetadata metadata)
        {

            var handlers = new List<OperationHandler>();
            var builder = new PeerOperationsBuilder(handlers,metadata);

            foreach (var config in configurators())
            {
                config.OnRegisteringOperations(builder);
            }

            Handlers = handlers.ToArray();
            _initialized = true;
        }

        public bool TryGet(ReadOnlySequence<byte> operationBuffer, [NotNullWhen(true)] out OperationHandler? handler)
        {
           
                Debug.Assert(Handlers!=null);


         
            //Probably doesn't make sense to use FrozenDictionary because of the number of operations, but we should probably keep that in mind while profiling.
            for(int i = 0; i< Handlers.Length; i++)
            {
                var candidate = Handlers[i];
                if(candidate.Operation.Equals(operationBuffer))
                {
                    handler = candidate;
                    return true;
                }
            }
            
            handler = null;
            return false;
        }

        
    }
}
