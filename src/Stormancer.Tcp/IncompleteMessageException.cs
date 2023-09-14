using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Tcp
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
            throw new IncompleteMessageException("Not enough data available. Expected={expected}, Available={available}");
        }
    }
}
