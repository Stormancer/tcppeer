using Stormancer.Networking.Reliable.Tcp;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    /// <summary>
    /// 
    /// </summary>
    public class MeshClient : IAsyncDisposable
    {
        private readonly PeerOptions _configuration;
        private readonly PipeOptions _pipeOptions;

        private readonly Counters _counters;

        private readonly HandlerStore _handlers;
        private PeerImpl _peer;

        /// <summary>
        /// Creates a new instance of <see cref="MeshClient"/>
        /// </summary>
        /// <param name="configuration"></param>
        public MeshClient(PeerOptions configuration)
        {
            _configuration = configuration;
            _pipeOptions = new PipeOptions(_configuration.MemoryPool);
            _counters = new Counters(_configuration.Name);
            _handlers = new HandlerStore(_configuration.OperationConfiguratorsGetter);
            if (_configuration.Metadata == null)
            {
                Metadata = PeerMetadata.CreateEmpty();
            }
            else
            {
                Metadata = _configuration.Metadata;
            }
        

            //_handlers.Initialize(Metadata);

            CreateInternalPeer();

        }

        [MemberNotNull("_peer")]
        private void CreateInternalPeer()
        {
            var socketOptions = _configuration.TransportsOptions.Features.Get<SocketTransportOptions>() ?? new SocketTransportOptions();
            var networkConnectionFactory = new TcpNetworkConnectionFactory(socketOptions, _configuration.LoggerFactory);
            var tcpSocketFactory = new TcpSocketFactory(_configuration.LoggerFactory, networkConnectionFactory, socketOptions);
            var transportsFactories = new[] { tcpSocketFactory };

            _peer = new PeerImpl(Metadata,transportsFactories.Cast<IConnectionListenerFactory>(), transportsFactories.Cast<IClientConnectionFactory>(), _configuration.TransportsOptions, _configuration.LoggerFactory);
        }


        /// <summary>
        /// Gets metadata of the local peer.
        /// </summary>
        public PeerMetadata Metadata { get; }

        /// <summary>
        /// Gets the id of the local client in the mesh.
        /// </summary>
        public PeerId Id => Metadata.PeerId;


        /// <summary>
        /// Starts the peer and bind to the local address.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return _peer.StartAsync(cancellationToken);
        }

        /// <summary>
        /// Connects the <see cref="MeshClient"/> to another peer.
        /// </summary>
        /// <param name="serverEndpoint"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task ConnectAsync(IPEndPoint serverEndpoint, CancellationToken cancellationToken)
        {
          
            return _peer.ConnectAsync(serverEndpoint,cancellationToken);
        }

        /// <summary>
        /// Creates a request but does not send it.
        /// </summary>
        /// <param name="destination"></param>
        /// <param name="operation"></param>
        /// <remarks>Not sending the request immediately enables writing the request content beforehand 
        /// and improves performance by avoiding unnecessary scheduling when all the content is already 
        /// available.
        /// </remarks>
        /// <returns></returns>
        public PeerRequest CreateRequest(PeerId destination, OperationId operation)
        {
            var request = new PeerRequest(this, destination, operation, _pipeOptions);

            return request;
        }

        /// <summary>
        /// Sends a request to the target peer immediately.
        /// </summary>
        /// <param name="destination"></param>
        /// <param name="operation"></param>
        /// <remarks>The request starts being processed before the consumer can write its content. 
        /// This starts a background process that can reduce performance when sending a lot of requests. 
        /// If the request content is known beforehand and of limited length, prefer using 
        /// <see cref="CreateRequest(PeerId, OperationId)"/>, writing the content then finally calling 
        /// <see cref="PeerRequest.Start()"/>.
        /// </remarks>
        /// <returns></returns>
        public PeerRequest SendRequest(PeerId destination, OperationId operation)
        {
            var rq = CreateRequest(destination, operation);

            rq.Start();
            return rq;
        }

        /// <summary>
        /// Broadcasts a request to all the peer in the network mesh.
        /// </summary>
        /// <param name="operation"></param>
        /// <returns></returns>
        public BroadcastRequest Broadcast(string operation)
        {
            var rq = CreateBroadcastRequest(operation);

            rq.Start();
            return rq;
        }

        /// <summary>
        /// Creates a broadcast request but does not send it.
        /// </summary>
        /// <param name="operation"></param>
        /// <remarks>Not sending the request immediately enables writing the request content beforehand
        /// and improves performance by avoiding unnecessary scheduling when all the content is already 
        /// available.
        /// </remarks>
        /// <returns></returns>
        public BroadcastRequest CreateBroadcastRequest(string operation)
        {
            return new BroadcastRequest();
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return _peer.DisposeAsync();
        }



        internal void StartRequest(PeerRequest peerRequest, Pipe contentPipe, Pipe responsePipe)
        {
            //_counters.Sent.Add(1);
            //if (peerRequest.Destination == Id) //Request send locally.
            //{
            //    if (_handlers.TryGet(new ReadOnlySequence<byte>(peerRequest.Operation.Buffer), out var handler))
            //    {
            //        _ = ExecuteHandler(peerRequest.Operation, contentPipe.Reader, contentPipe.Writer, handler);
            //    }
            //}

        }

      
    }
}
