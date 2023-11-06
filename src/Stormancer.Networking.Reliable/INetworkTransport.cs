using Microsoft.AspNetCore.Connections;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    /// <summary>
    /// Provides methods to determine if a factory can support an endpoint.
    /// </summary>
    public interface INetworkCapabilities
    {

        /// <summary>
        /// Gets a boolean indicating if the factory can bind/connect to an <see cref="EndPoint"/>.
        /// </summary>
        /// <param name="endPoint"></param>
        /// <returns></returns>
        bool CanBind(EndPoint endPoint);
    }
    /// <summary>
    /// Provides methods to bind to a network <see cref="EndPoint"/>.
    /// </summary>
    public interface IConnectionListenerFactory : INetworkCapabilities
    {

        /// <summary>
        /// Binds the transport to an <see cref="EndPoint"/>.
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="config"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        ValueTask<IConnectionListener> BindAsync(EndPoint endpoint, EndPointConfig? config, CancellationToken cancellationToken);

    }
    /// <summary>
    /// Provides methods to connect ot a network <see cref="EndPoint"/>.
    /// </summary>
    public interface IClientConnectionFactory : INetworkCapabilities
    {

        /// <summary>
        /// Connects asynchronously to an endpoint.
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="config"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>The <see cref="RemotePeer"/> object representing the peer, or null if connection failed.</returns>
        ValueTask<NetworkConnection> ConnectAsync(EndPoint endpoint, EndPointConfig? config, CancellationToken cancellationToken);


    }


    /// <summary>
    /// A network listener that can accept connections.
    /// </summary>
    public interface IConnectionListener : IAsyncDisposable
    {
        /// <summary>
        /// Wait asynchronously for a new connection.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        ValueTask<NetworkConnection?> AcceptAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the local endpoint this listener is bound to.
        /// </summary>
        EndPoint LocalEndpoint { get; }

        /// <summary>
        /// Stops listening for incoming connections.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="ValueTask"/> that represents the unbind operation.</returns>
        ValueTask UnbindAsync(CancellationToken cancellationToken = default);
    }




    /// <summary>
    /// A connection with a remote peer.
    /// </summary>
    /// <param name="localEndpoint"></param>
    /// <param name="remoteEndpoint"></param>
    /// <param name="memoryPool"></param>
    /// <param name="features"></param>
    public abstract class NetworkConnection(EndPoint localEndpoint, EndPoint remoteEndpoint,MemoryPool<byte> memoryPool,IFeatureCollection features) : IAsyncDisposable
    {
        /// <summary>
        /// Gets the remote <see cref="EndPoint"/> of the connection.
        /// </summary>
        public EndPoint RemoteEndpoint { get; } = remoteEndpoint;

        /// <summary>
        /// Gets the local <see cref="EndPoint"/> of the connection.
        /// </summary>
        public EndPoint LocalEndpoint { get; } = localEndpoint;

        /// <summary>
        /// Gets a <see cref="IDuplexPipe"/> object to read incoming data and write outgoing data.
        /// </summary>
        public abstract IDuplexPipe Transport { get; set; }

        /// <summary>
        /// Gets the <see cref="MemoryPool{T}"/> used by the connection to allocate memory.
        /// </summary>
        public MemoryPool<byte> MemoryPool { get; } = memoryPool;

        /// <summary>
        /// Triggered when the connection closes.
        /// </summary>
        public CancellationToken ConnectionClosed { get; protected set; }
        /// <summary>
        /// Aborts the connection.
        /// </summary>
        public abstract void Close(ConnectionAbortedException reason);

        /// <summary>
        /// Aborts the connection.
        /// </summary>
        public void Close() => Close(new ConnectionAbortedException("requestedByPeer"));

        /// <inheritdoc/>
        public abstract ValueTask DisposeAsync();

        private string? _connectionId;

        /// <summary>
        /// Gets a <see cref="string"/> containing an unique id for the connection.
        /// </summary>
        public string ConnectionId
        {
            get => _connectionId ??= CorrelationIdGenerator.GetNextId();
        }

        /// <summary>
        /// Features of the <see cref="NetworkConnection"/>.
        /// </summary>
        public IFeatureCollection Features { get; } = features;
    }
}
