using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Tcp
{
    /// <summary>
    /// Structure describing disconnection reasons
    /// </summary>
    /// <param name="Reason"></param>
    /// <param name="Error"></param>
    public record DisconnectionReason(string Reason, Exception? Error);

    public class RemotePeer
    {
    }
}
