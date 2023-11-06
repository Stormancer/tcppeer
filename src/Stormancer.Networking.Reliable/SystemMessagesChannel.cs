using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    /// <summary>
    /// A channel used to share system message between components of the peer.
    /// </summary>
    public class SystemMessagesChannel
    {
        /// <summary>
        /// Sends a message in the channel.
        /// </summary>
        /// <param name="message"></param>
        public void SendMessage(SystemMessage message)
        {
            OnSystemMessageReceived?.Invoke(message);
        }

        /// <summary>
        /// Event fired whenever a message is sent in the channel.
        /// </summary>
        public event Action<SystemMessage>? OnSystemMessageReceived;
    }

    /// <summary>
    /// A system message processed by the peer.
    /// </summary>
    public class SystemMessage
    {
        /// <summary>
        /// Gets the type of the message.
        /// </summary>
        public SystemMessageType Type { get; }

        /// <summary>
        /// Gets custom context associated with the message. It depends on the type.
        /// </summary>
        public object? Context { get; }
    }
    
    /// <summary>
    /// Types of system messages.
    /// </summary>
    public enum SystemMessageType
    {
        /// <summary>
        /// A peer connected with the local peer.
        /// </summary>
        RemotePeerConnected,

        /// <summary>
        /// A peer disconnected from the local peer.
        /// </summary>
        RemotePeerDisconnected,

        /// <summary>
        /// The network connection of the local peer has been disabled.
        /// </summary>
        NetworkDisabled,

        /// <summary>
        /// The network connection of the local peer has been enabled.
        /// </summary>
        NetworkEnabled,


    }
}
