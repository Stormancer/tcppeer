using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    internal class OperationHandler
    {
        public OperationHandler(OperationId operationId, Func<RequestContext, ValueTask> handler)
        {
            Operation = operationId;
            Handler = handler;
            
        }
        public Func<RequestContext, ValueTask> Handler { get; }
        public OperationId Operation { get; }
    }
}
