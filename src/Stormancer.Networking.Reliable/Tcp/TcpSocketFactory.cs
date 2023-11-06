using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable.Tcp
{
    /// <summary>
    /// 
    /// </summary>
    public class SocketTransportOptions
    {
        /// <summary>
        /// Gets or sets the maximum number of pending connections to this socket.
        /// </summary>
        public int BackLog { get; set; } = 512;

        /// <summary>
        /// The number of I/O queues used to process requests. Set to 0 to directly schedule I/O to the ThreadPool.
        /// </summary>
        /// <remarks>
        /// Defaults to <see cref="Environment.ProcessorCount" /> rounded down and clamped between 1 and 16.
        /// </remarks>
        public int IOQueueCount { get; set; } = Math.Min(Environment.ProcessorCount, 16);

        /// <summary>
        /// Gets or sets the maximum unconsumed incoming bytes the transport will buffer.
        /// </summary>
        public long? MaxReadBufferSize { get; set; } = 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum outgoing bytes the transport will buffer before applying write backpressure.
        /// </summary>
        public long? MaxWriteBufferSize { get; set; } = 64 * 1024;


        internal Func<MemoryPool<byte>> MemoryPoolFactory { get; set; } = () => MemoryPool<byte>.Shared;
    }

    internal class TcpSocketFactory(ILoggerFactory logger, INetworkConnectionFactory networkConnectionFactory, SocketTransportOptions options) : IConnectionListenerFactory, IClientConnectionFactory
    {
        private readonly ILoggerFactory _logger = logger;
        private readonly SocketTransportOptions _options = options;
        private readonly INetworkConnectionFactory _networkConnectionFactory = networkConnectionFactory;


        internal static Socket CreateSocketForEndpoint(EndPoint endpoint)
        {
            var socket = endpoint switch
            {
                IPEndPoint ip => new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp),
                UnixDomainSocketEndPoint unix => new Socket(unix.AddressFamily, SocketType.Stream, ProtocolType.Unspecified),
                _ => throw new NotSupportedException()
            };
            ApplyDefaultSocketOptions(socket);

            return socket;
        }

        internal static void ApplyDefaultSocketOptions(Socket socket)
        {
            if (socket.LocalEndPoint is IPEndPoint)
            {
                socket.NoDelay = true;
            }
        }

        public bool CanBind(EndPoint endpoint)
        {
            return endpoint switch
            {
                IPEndPoint _ => true,
                UnixDomainSocketEndPoint _ => true,
                _ => false
            };
        }

        public async ValueTask<NetworkConnection> ConnectAsync(EndPoint endpoint, EndPointConfig? config, CancellationToken cancellationToken)
        {
            var socket = CreateSocketForEndpoint(endpoint);

            await socket.ConnectAsync(endpoint, cancellationToken);

            return _networkConnectionFactory.Create(socket, config, true);
        }

        ValueTask<IConnectionListener> IConnectionListenerFactory.BindAsync(EndPoint endpoint, EndPointConfig? config, CancellationToken cancellationToken)
        {
            var transport = new TcpSocketListener(endpoint, _logger, _networkConnectionFactory, _options, config);
            transport.Bind();

            return ValueTask.FromResult<IConnectionListener>(transport);
        }
    }



    internal class TcpSocketListener(EndPoint endPoint, ILoggerFactory logger, INetworkConnectionFactory factory, SocketTransportOptions options, EndPointConfig? endPointConfig) : IConnectionListener
    {
        private readonly ILogger _logger = logger.CreateLogger<TcpSocketListener>();
        private readonly INetworkConnectionFactory _factory = factory;
        private readonly SocketTransportOptions _options = options;
        private readonly EndPointConfig? _endPointConfig = endPointConfig;
        private Socket? _socket;
        public EndPoint LocalEndpoint { get; private set; } = endPoint;

        public async ValueTask<NetworkConnection?> AcceptAsync(CancellationToken cancellationToken)
        {
            if (_socket == null)
            {
                throw new InvalidOperationException("Bind must be called before AcceptAsync().");
            }
            while (true)
            {
                try
                {
                    var acceptSocket = await _socket.AcceptAsync(cancellationToken);

                    TcpSocketFactory.ApplyDefaultSocketOptions(acceptSocket);

                    try
                    {
                        return _factory.Create(acceptSocket, _endPointConfig, false);
                    }
                    catch (InvalidOperationException ex)
                    {
                        SocketLogs.FailedCreatingNetworkConnection(_logger, acceptSocket.RemoteEndPoint, ex);
                        acceptSocket.Close();
                    }
                }
                catch (ObjectDisposedException)
                {
                    return null;
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.OperationAborted)
                {
                    return null;
                }
                catch (SocketException)
                {
                    SocketLogs.ConnectionReset(_logger, "<NULL>");
                }

            }
        }




        public void Bind()
        {
            if (_socket != null)
            {
                throw new InvalidOperationException("Socket already bound.");
            }
            var socket = TcpSocketFactory.CreateSocketForEndpoint(LocalEndpoint);


            socket.Bind(LocalEndpoint);

            socket.Listen();

            Debug.Assert(socket.LocalEndPoint != null);

            LocalEndpoint = socket.LocalEndPoint;
            _socket = socket;

        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return UnbindAsync();
        }

        public ValueTask UnbindAsync(CancellationToken cancellationToken = default)
        {
            _socket?.Dispose();
            return default;
        }

    }



}
