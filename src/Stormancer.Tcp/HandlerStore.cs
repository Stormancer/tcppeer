using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Tcp
{
    internal class HandlerStore
    {
        public HandlerStore(Func<IEnumerable<INetworkPeerConfigurator>> configurators)
        {
            this.configurators = configurators;
        }
            
        public void Add(string operation, Func<RequestContext, Task> handler)
        {
            Handlers.Add(new OperationHandler(operation, handler));
        }
        public List<OperationHandler> Handlers { get; } = new List<OperationHandler>();

        private bool _initialized = false;
        private readonly Func<IEnumerable<INetworkPeerConfigurator>> configurators;
      
        public void Initialize(PeerMetadata metadata)
        {
          
            var builder = new ApiBuilder(this,metadata);

            foreach (var config in configurators())
            {
                config.Configure(builder);
            }
            _initialized = true;
        }

        public bool TryGet(ReadOnlySequence<byte> operation, [NotNullWhen(true)] out OperationHandler? handler)
        {
           
                Debug.Assert(_initialized);
            

            //We could probably do a lot better, but not sure that !IsSingleSegment is hit often enough to matter?
            ReadOnlySpan<byte> span;
            if (operation.IsSingleSegment)
            {
                span = operation.FirstSpan;
            }
            else
            {

                span = new Span<byte>(operation.ToArray());
            }
            for (int i = 0; i < Handlers.Count; i++)
            {
                var current = Handlers[i];
                if (UnsafeCompare(span, new ReadOnlySpan<byte>(current.Utf8Representation)) == 0)
                {
                    handler = current;
                    return true;
                }
            }
            handler = default;
            return false;
        }

        // Copyright (c) 2008-2013 Hafthor Stefansson
        // Distributed under the MIT/X11 software license
        // Ref: http://www.opensource.org/licenses/mit-license.php.
        static unsafe int UnsafeCompare(ReadOnlySpan<byte> a1, ReadOnlySpan<byte> a2)
        {
            if (a1 == a2) return 0;
            if (a1 == null)
            {
                return -1;
            }
            else if (a2 == null)
            {
                return 1;
            }
            else if (a1.Length != a2.Length)
            {
                return a1.Length - a2.Length;
            }

            fixed (byte* p1 = a1, p2 = a2)
            {
                byte* x1 = p1, x2 = p2;
                int l = a1.Length;
                for (int i = 0; i < l / 8; i++, x1 += 8, x2 += 8)
                {
                    if (*(long*)x1 != *(long*)x2)
                    {
                        return *(long*)x1 - *(long*)x2 > 0 ? 1 : -1;
                    }
                }

                if ((l & 4) != 0)
                {
                    if (*(int*)x1 != *(int*)x2)
                    {
                        return *(int*)x1 - *(int*)x2;
                    }
                    x1 += 4; x2 += 4;
                }
                if ((l & 2) != 0)
                {
                    if (*(short*)x1 != *(short*)x2)
                    {
                        return *(short*)x1 - *(short*)x2;
                    }
                    x1 += 2; x2 += 2;
                }

                if ((l & 1) != 0)
                {
                    if (*x1 != *x2)
                    {
                        return *x1 - *x2;
                    }
                }
                return 0;
            }
        }
    }
}
