using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    /// <summary>
    /// Provides a method to create a <see cref="NetworkConnection"/> from a <see cref="Socket"/>.
    /// </summary>
    public interface INetworkConnectionFactory
    {
        /// <summary>
        /// Creates a connection from a socket.
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="endPointConfig"></param>
        /// <param name="isClient"></param>
        /// <returns></returns>
        NetworkConnection Create(Socket socket,EndPointConfig? endPointConfig,bool isClient);
    }


   
}
