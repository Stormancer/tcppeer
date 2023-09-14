using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Tcp
{
    internal class OperationHandler
    {
        public OperationHandler(string operation, Func<RequestContext, Task> handler)
        {
            Operation = operation;
            Handler = handler;
            var byteCount = Encoding.UTF8.GetByteCount(operation);
            var data = new byte[1 + byteCount];
            data[0] = (byte)byteCount;
            Encoding.UTF8.GetBytes(operation, data.AsSpan().Slice(1));
            Utf8Representation = data;
        }
        public Func<RequestContext, Task> Handler { get; }
        public string Operation { get; }
        public byte[] Utf8Representation { get; }
    }
}
