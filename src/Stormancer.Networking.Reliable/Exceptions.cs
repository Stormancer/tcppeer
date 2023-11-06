using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    /// <summary>
    /// The exception that is thrown when the peer receives an incomplete message.  
    /// </summary>
    public class IncompleteMessageException : Exception
    {
        internal IncompleteMessageException(string? message) : base(message)
        {

        }


        internal static void ThrowException(long available, long expected)
        {
            throw new IncompleteMessageException($"Not enough data available. Expected={expected}, Available={available}");
        }
    }

    /// <summary>
    /// Exception thrown when an operation that doesn't exist on the remote peer is called.
    /// </summary>
    public class OperationNotFoundException : Exception
    {
        /// <summary>
        /// Creates a new instance of <see cref="OperationNotFoundException"/>.
        /// </summary>
        /// <param name="operation"></param>
        public OperationNotFoundException(OperationId operation) : base($"Operation {operation} not found.")
        {

            Operation = operation;
        }




        /// <summary>
        /// Gets the id of the operation that generated the error.
        /// </summary>
        public OperationId Operation { get; }
    }

}
