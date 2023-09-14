using Microsoft.Extensions.ObjectPool;
using Pipelines.Sockets.Unofficial;
using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Threading.Tasks.Sources;
using System.Xml.Linq;

namespace Stormancer.Tcp.Deprecated
{
    /// <summary>
    /// Provides a way to configures a TCP Peer instance.
    /// </summary>
    public interface ITcpPeerConfigurator
    {
        /// <summary>
        /// Called to configure the TCP Peer.
        /// </summary>
        /// <param name="builder"></param>
        void Configure(ITcpPeerApiBuilder builder);
    }

    /// <summary>
    /// Tcp peer configuration builder.
    /// </summary>
    public interface ITcpPeerApiBuilder
    {
        /// <summary>
        /// Id of the tcp peer.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Adds a request handler to the peer.
        /// </summary>
        /// <param name="operation"></param>
        /// <param name="handler"></param>
        /// <returns></returns>
        ITcpPeerApiBuilder ConfigureHandler(string operation, Func<TcpPeerRequestContext, Task> handler);
    }

    /// <summary>
    /// Represents an exception thrown by <see cref="TcpPeer"/>
    /// </summary>
    public class RequestException : Exception
    {
        /// <summary>
        /// Creates a <see cref="RequestException"/> instance.
        /// </summary>
        /// <param name="code"></param>
        public RequestException(int code) : base($"TCP Request Error: {code}")
        {
            Code = code;
        }

        /// <summary>
        /// Creates an exception object.
        /// </summary>
        /// <param name="code"></param>
        /// <param name="innerException"></param>
        public RequestException(int code, Exception innerException) : base($"TCP Request Error: {code}", innerException)
        {

        }

        /// <summary>
        /// 
        /// </summary>
        public int Code { get; }


    }

    /// <summary>
    /// Standard error codes for <see cref="RequestException"/> 
    /// </summary>
    public static class TcpRequestErrorCodes
    {
        /// <summary>
        /// Connection lost.
        /// </summary>
        public const int ConnectionLost = 001;

        /// <summary>
        /// Operation not found.
        /// </summary>
        public const int OperationNotFound = 404;

        /// <summary>
        /// An exception occured during the operation.
        /// </summary>
        public const int OperationError = 500;

        /// <summary>
        /// Operation was canceled.
        /// </summary>
        public const int OperationCanceled = 510;
    }

    /// <summary>
    /// Represents a TCP Peer message header.
    /// </summary>
    internal struct MessageHeader
    {
        /// <summary>
        /// Gets the length of the header.
        /// </summary>
        public long Length { get; internal set; }

        /// <summary>
        /// Gets a boolean indicating if the header is valid.
        /// </summary>
        public bool IsValid => Length >= 0;

        /// <summary>
        /// Gets the id of the request the message is a part of.
        /// </summary>
        public Guid Id { get; internal set; }

        /// <summary>
        /// Gets the type of the message.
        /// </summary>
        public MessageType Type { get; internal set; }
    }


    internal struct Message
    {

        public MessageHeader Header { get; set; }
        public ReadOnlySequence<byte> Body { get; set; }
        public RemotePeer Origin { get; internal set; }
    }

    /// <summary>
    /// Possible message types.
    /// </summary>
    internal enum MessageType : byte
    {
        Empty = 0,
        /// <summary>
        /// The message is a request start.
        /// </summary>
        Begin = 0B_1000_0000,
        /// <summary>
        /// The message contains content.
        /// </summary>
        Content = 0B_0100_0000,
        /// <summary>
        /// The message signals the end of a stream.
        /// </summary>
        End = 0B_0010_0000,

        Request = 0B_0001_0000,
        Response = 0B_0000_1000,

        /// <summary>
        /// The message signals an error.
        /// </summary>
        Error = 0B_0000_0001,

        /// <summary>
        /// The request was canceled.
        /// </summary>
        Canceled = 0B_0000_0011,
    }

    public struct BroadcastRequestHeader
    {

        public Guid Id { get; set; }
        public string Operation { get; set; }
        public IPEndPoint Origin { get; set; }

        public DateTime Date { get; set; }

        public PeerMetadata Metadata { get; set; }

        //public Dictionary<string, string> Metadata { get; set; }
    }
    public record struct BroadcastResponseHeader(bool Success, IPEndPoint? Origin, ushort Length);


    /// <summary>
    /// Represents a request propagated to all nodes in the cluster graph.
    /// </summary>
    public class BroadcastRequest : IDisposable
    {
        internal Pipe? sendPipe;
        internal Channel<BroadcastResponse> responseChannel = Channel.CreateUnbounded<BroadcastResponse>();


        internal void Init()
        {

            sendPipe = new Pipe();
        }

        public Guid Id { get; } = Guid.NewGuid();


        public PipeWriter Writer
        {
            get
            {
                Debug.Assert(sendPipe != null);
                return sendPipe.Writer;
            }
        }

        private Dictionary<IPEndPoint, BroadcastResponse> _responses = new Dictionary<IPEndPoint, BroadcastResponse>();

        internal BroadcastResponse GetOrAddResponse(IPEndPoint origin)
        {
            if (_responses.TryGetValue(origin, out var response))
            {
                return response;
            }
            else
            {

                response = new BroadcastResponse(origin);

                response.Init();
                _responses.Add(origin, response);
                responseChannel.Writer.WriteAsync(response);
                return response;
            }

        }

        /// <summary>
        /// Gets the responses stream of the broadcast request.
        /// </summary>
        /// <returns></returns>
        public IAsyncEnumerable<BroadcastResponse> GetResponses()
        {

            var reader = responseChannel.Reader;
            static async IAsyncEnumerable<BroadcastResponse> GetResponsesImpl(ChannelReader<BroadcastResponse> reader)
            {
                while (await reader.WaitToReadAsync())
                {
                    while (reader.TryRead(out var item))
                    {
                        yield return item;
                    }
                }
            }
            return GetResponsesImpl(reader);

        }




        private bool _isDisposed = false;

        public void Dispose()
        {
            Debug.Assert(sendPipe != null);
            if (!_isDisposed)
            {
                _isDisposed = true;

                sendPipe.Writer.Complete();

                foreach (var response in _responses)
                {
                    response.Value.Dispose();
                }
                TryReset();
            }
        }

        private bool _readerComplete;
        internal void SetReaderComplete(Exception? ex = null)
        {
            Debug.Assert(sendPipe != null);
            if (!_readerComplete)
            {
                _readerComplete = true;
                sendPipe.Reader.Complete(ex);
                TryReset();
            }
        }

        internal void SetWritersComplete(Exception? ex = null)
        {
            responseChannel.Writer.Complete(ex);
            foreach (var response in _responses.Values)
            {
                response.SetWriterComplete(ex);
            }
        }

        private void TryReset()
        {
            if (_isDisposed && _readerComplete && sendPipe != null)
            {

                sendPipe = null;
            }
        }
    }

    /// <summary>
    /// Represents a response to an <see cref="BroadcastRequest"/>.
    /// </summary>
    public class BroadcastResponse : IDisposable
    {
        internal Pipe? pipe;



        internal void Init()
        {

            pipe = new Pipe();
        }

        internal BroadcastResponse(IPEndPoint endpoint)
        {
            Origin = endpoint;
        }


        public IPEndPoint Origin { get; }

        public PipeReader Reader
        {
            get
            {
                Debug.Assert(pipe != null);
                return pipe.Reader;
            }
        }

        //public List<BroadcastResponseHeader> Headers { get; } = new List<BroadcastResponseHeader>();

        private bool _isDisposed = false;
        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;

                pipe.Reader.Complete();
                TryReset();
            }
        }

        private void TryReset()
        {
            if (_writeComplete && _isDisposed && pipe != null)
            {

                pipe = null;
            }
        }


        private bool _writeComplete;

        internal void SetWriterComplete(Exception? ex = null)
        {
            if (!_writeComplete)
            {
                _writeComplete = true;
                pipe.Writer.Complete(ex);
                TryReset();
            }

        }
    }



    /// <summary>
    /// Represents a response to a request made using a <see cref="TcpPeer"/> instance.
    /// </summary>
    public class TcpRequest : IDisposable
    {
        internal Pipe? receivePipe;
        internal Pipe? sendPipe;

        private bool _isDisposed;
        private bool _isInputReadCompleted;
        private bool _isOutputWriteCompleted;
        private bool _cleanedUp;
        private Action? _onDispose;

        public string Stack { get; set; }
        internal void Init(Guid id, string operation, RemotePeer? remotePeer, Action onDispose)
        {
            if (Debugger.IsAttached)
            {
                //Stack = new System.Diagnostics.StackTrace(1, true).ToString();
            }
            _onDispose = onDispose;
            _isDisposed = false;
            _isInputReadCompleted = false;
            _isOutputWriteCompleted = false;
            _cleanedUp = false;



            _operation = operation ?? throw new ArgumentNullException(nameof(operation));
            RemotePeer = remotePeer;
            Id = id;
            sendPipe = new Pipe();

            receivePipe = new Pipe();

            lastFlush = default;

            if (RemotePeer != null)
            {
                lock (RemotePeer.RequestsSentTo)
                {

                    RemotePeer.RequestsSentTo.Add(new PendingOperationInfo(id, this));
                }
            }

        }


        /// <summary>
        /// Gets a <see cref="PipeReader"/> instance used to read data from the request.
        /// </summary>
        public PipeReader Reader
        {
            get
            {
                Debug.Assert(receivePipe != null);
                return receivePipe.Reader;
            }
        }

        /// <summary>
        /// Gets a <see cref="PipeWriter"/> instance used to write data in the request.
        /// </summary>
        public PipeWriter Writer
        {
            get
            {
                Debug.Assert(sendPipe != null);
                return sendPipe.Writer;
            }
        }

        private string? _operation;

        /// <summary>
        /// Operation of the request.
        /// </summary>
        public string Operation
        {
            get
            {
                Debug.Assert(_operation != null);
                return _operation;
            }
        }

        /// <summary>
        /// Remote peer the request is sent to.
        /// </summary>
        public RemotePeer? RemotePeer { get; private set; }

        /// <summary>
        /// Id of the request associated with the response.
        /// </summary>
        public Guid Id { get; private set; }


        //private DefaultObjectPool<TcpRequest>? responsePool;
        internal ValueTask<FlushResult> lastFlush;


        internal object SyncRoot = new object();
        /// <summary>
        /// Custom data associated with the request.
        /// </summary>
        public object? State { get; set; }

        /// <summary>
        /// Gets a <see cref="bool"/> indicating whether the request was disposed.
        /// </summary>
        public bool IsDisposed => _isDisposed;


        internal void Reset()
        {

            Debug.Assert(sendPipe != null && receivePipe != null);
            State = null;

            receivePipe = null;
            sendPipe = null;
            Id = default;
            _operation = default;

        }


        /// <summary>
        /// Disposes the response object and releases associated resources.
        /// </summary>
        public void Dispose()
        {
            bool shouldDispose = false;
            lock (SyncRoot)
            {
                if (!_isDisposed)
                {
                    _isDisposed = true;
                    shouldDispose = true;

                    Debug.Assert(sendPipe != null && receivePipe != null);
                    sendPipe.Writer.Complete();
                    receivePipe.Reader.Complete();

                    TryCleanup();
                }
            }

            if (shouldDispose)
            {
                _onDispose?.Invoke();



            }
        }

        internal void SetInputCompleted(Exception? ex = null)
        {
            lock (SyncRoot)
            {
                if (!_isInputReadCompleted)
                {
                    _isInputReadCompleted = true;
                    Debug.Assert(sendPipe != null && receivePipe != null);
                    sendPipe.Reader.Complete(ex);
                    TryCleanup();
                }
            }
        }

        internal void SetOutputCompleted(Exception? ex = null)
        {
            lock (SyncRoot)
            {
                if (!_isOutputWriteCompleted)
                {
                    _isOutputWriteCompleted = true;
                    Debug.Assert(sendPipe != null && receivePipe != null);
                    receivePipe.Writer.Complete(ex);
                    TryCleanup();
                }
            }
        }

        private void TryCleanup()
        {


            if (_isDisposed && _isOutputWriteCompleted && _isInputReadCompleted && !_cleanedUp)
            {
                _cleanedUp = true;

                if (RemotePeer != null)
                {
                    lock (RemotePeer.RequestsSentTo)
                    {
                        RemotePeer.RequestsSentTo.Remove(new PendingOperationInfo(Id, null));
                    }
                }

                //this.responsePool?.Return(this);
            }
        }
        private static long WriteToOutput(PipeWriter output, ref Message message)
        {
            int remaining = (int)message.Header.Length;
            int written = 0;
            while (remaining > 0)
            {
                var span = output.GetMemory(remaining).Span;
                var length = remaining < span.Length ? remaining : span.Length;
                message.Body.Slice(written, length).CopyTo(span);
                output.Advance(length);
                written += length;
                remaining -= length;
            }
            return message.Header.Length;

        }


        internal ValueTask<FlushResult> ProcessResponseContent(Message message)
        {

            Debug.Assert(receivePipe != null);

            lock (SyncRoot)
            {
                if (!_isOutputWriteCompleted)
                    WriteToOutput(receivePipe.Writer, ref message);
            }


            //await response.receivePipe.Writer.FlushAsync();
            return receivePipe.Writer.FlushAsync();

        }
    }

    internal class OperationHandler
    {
        public OperationHandler(string operation, Func<TcpPeerRequestContext, Task> handler)
        {
            Operation = operation;
            Handler = handler;
            var byteCount = Encoding.UTF8.GetByteCount(operation);
            var data = new byte[1 + byteCount];
            data[0] = (byte)byteCount;
            Encoding.UTF8.GetBytes(operation, data.AsSpan().Slice(1));
            Utf8Representation = data;
        }
        public Func<TcpPeerRequestContext, Task> Handler { get; }
        public string Operation { get; }
        public byte[] Utf8Representation { get; }
    }

    /// <summary>
    /// Remote peer.
    /// </summary>
    public class RemotePeer
    {
        internal RemotePeer(SocketConnection? connection, Socket? localSocket, PeerMetadata metadata, bool isRemoteConnectedClient)
        {
            Connection = connection;
            LocalSocket = localSocket;
            Endpoint = (IPEndPoint?)Connection?.Socket?.RemoteEndPoint;
            Metadata = metadata;

        }


        /// <summary>
        /// The local socket that received through which the connection was established, if the peer is an incoming connection.
        /// </summary>
        public Socket? LocalSocket { get; }

        /// <summary>
        /// Gets the peer endpoint.
        /// </summary>
        public IPEndPoint? Endpoint { get; }


        internal SocketConnection? Connection { get; }

        internal HashSet<PendingOperationInfo> RequestsSentTo { get; } = new HashSet<PendingOperationInfo>();
        internal HashSet<PendingOperationInfo> RequestsReceivedFrom { get; } = new HashSet<PendingOperationInfo>();


        public PeerMetadata Metadata { get; internal set; }
        /// <summary>
        /// Custom state object.
        /// </summary>
        public object? State { get; set; }

        /// <summary>
        /// Is the remote peer a client that connected to this peer.
        /// </summary>
        public bool IsClient
        {
            get;
        }
    }

    public struct PendingOperationInfo : IEquatable<PendingOperationInfo>
    {
        public PendingOperationInfo(Guid id, object? ctx)
        {
            Id = id;
            Context = ctx;
        }
        public override bool Equals(object? obj)
        {
            if (obj is PendingOperationInfo)
            {
                return Equals((PendingOperationInfo)obj);
            }
            else
            {
                return false;
            }
        }

        public bool Equals(PendingOperationInfo other)
        {
            return other.Id == Id;
        }

        public Guid Id { get; }
        public object? Context { get; }
    }




    /// <summary>
    /// A <see cref="TcpPeer"/> instance provides a request response model on top of TCP sockets using the <see cref="System.IO.Pipelines.Pipe"/> API.
    /// </summary>
    public class TcpPeer : IDisposable
    {

        private class ApiBuilder : ITcpPeerApiBuilder
        {
            private readonly HandlerStore server;
            public ApiBuilder(HandlerStore server, string id)
            {
                this.server = server;
                this.Id = id;
            }

            public string Id { get; }

            public ITcpPeerApiBuilder ConfigureHandler(string operation, Func<TcpPeerRequestContext, Task> handler)
            {
                server.Add(operation, handler);
                return this;
            }
        }
        private struct Operation
        {
            public PipeWriter output;
            public MessageType type;
            public Guid guid;
            public ReadOnlySequence<byte> buffer;

            public IDisposable? Disposable;

            public void Write()
            {
                Debug.Assert(output != null);
                Debug.Assert(guid != default);
                var length = (int)buffer.Length;
                var span = output.GetMemory(1 + 16 + 4 + length).Span;

                //Write header
                span[0] = (byte)type;
                guid.TryWriteBytes(span.Slice(1, 16));
                BitConverter.TryWriteBytes(span.Slice(17, 4), length);


                buffer.CopyTo(span.Slice(21));


                //Advance output
                output.Advance(1 + 16 + 4 + length);

                if (Disposable != null)
                {
                    Disposable.Dispose();
                }

                //List<string> list;
                //lock (_debug)
                //{
                //    if (!_debug.TryGetValue(guid.ToString(), out list))
                //    {
                //        list = new List<string>();
                //        _debug[guid.ToString()] = list;
                //    }

                //    list.Add($"Sending : {type.ToString()}");
                //}
            }
        }
        private class OperationWriter : IValueTaskSource
        {
            private static readonly Action<object?> CallbackCompleted = _ => { Debug.Assert(false, "Should not be invoked"); };

            internal short token;
            private readonly DefaultObjectPool<OperationWriter> pool;
            public PipeWriter? output;

            public MessageType type;
            public Guid guid;
            public ReadOnlySequence<byte> buffer;

            private bool _isComplete;

            public OperationWriter(DefaultObjectPool<OperationWriter> pool)
            {
                this.pool = pool;
            }



            public void GetResult(short token)
            {
                Interlocked.Increment(ref _getResultCalled);
                if (token != this.token)
                    ThrowMultipleContinuations();

                var exception = this.exception;
                if (exception != null)
                {
                    throw exception;
                }

                Reset();
            }

            public void Init()
            {

                Debug.Assert(!Volatile.Read(ref _initialized));
                Debug.Assert(Volatile.Read(ref continuation) == null, $"Expected null continuation to indicate reserved for use");
                _getResultCalled = 0;
                _isComplete = false;
                _initialized = true;
            }
            private int _getResultCalled;


            public ValueTaskSourceStatus GetStatus(short token)
            {

                if (token != this.token)
                    ThrowMultipleContinuations();

                if (!_isComplete)
                {
                    return ValueTaskSourceStatus.Pending;
                }
                else
                {

                    return exception != null ? ValueTaskSourceStatus.Faulted : ValueTaskSourceStatus.Succeeded;
                }
            }

            private Action<object?>? continuation;
            private Exception? exception;
            private object? state;
            private ExecutionContext? executionContext;
            private object? scheduler;
            private bool _initialized;

            public void ThrowMultipleContinuations()
            {
                throw new InvalidOperationException("Multiple awaiters are not allowed");
            }

            public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            {
                Debug.Assert(_initialized);
                if (token != this.token)
                    ThrowMultipleContinuations();

                if ((flags & ValueTaskSourceOnCompletedFlags.FlowExecutionContext) != 0)
                {
                    this.executionContext = ExecutionContext.Capture();
                }

                if ((flags & ValueTaskSourceOnCompletedFlags.UseSchedulingContext) != 0)
                {
                    SynchronizationContext? sc = SynchronizationContext.Current;
                    if (sc != null && sc.GetType() != typeof(SynchronizationContext))
                    {
                        this.scheduler = sc;
                    }
                    else
                    {
                        TaskScheduler ts = TaskScheduler.Current;
                        if (ts != TaskScheduler.Default)
                        {
                            this.scheduler = ts;
                        }
                    }
                }

                // Remember current state
                this.state = state;
                // Remember continuation to be executed on completed (if not already completed, in case of which
                // continuation will be set to CallbackCompleted)
                Debug.Assert(_initialized);
                var previousContinuation = Interlocked.CompareExchange(ref this.continuation, continuation, null);
                if (previousContinuation != null)
                {
                    if (!ReferenceEquals(previousContinuation, CallbackCompleted))
                        ThrowMultipleContinuations();

                    // Lost the race condition and the operation has now already completed.
                    // We need to invoke the continuation, but it must be asynchronously to
                    // avoid a stack dive.  However, since all of the queueing mechanisms flow
                    // ExecutionContext, and since we're still in the same context where we
                    // captured it, we can just ignore the one we captured.
                    executionContext = null;
                    this.state = null; // we have the state in "state"; no need for the one in UserToken
                    InvokeContinuation(continuation, state, forceAsync: true);
                }
            }

            private void InvokeContinuation(Action<object?>? continuation, object? state, bool forceAsync)
            {
                if (continuation == null)
                    return;

                object? scheduler = this.scheduler;
                this.scheduler = null;
                if (scheduler != null)
                {
                    if (scheduler is SynchronizationContext sc)
                    {
                        sc.Post(s =>
                        {
                            var t = (Tuple<Action<object?>, object?>)s!;
                            t.Item1(t.Item2);
                        }, Tuple.Create(continuation, state));
                    }
                    else
                    {
                        Debug.Assert(scheduler is TaskScheduler, $"Expected TaskScheduler, got {scheduler}");
                        Task.Factory.StartNew(continuation, state, CancellationToken.None, TaskCreationOptions.DenyChildAttach, (TaskScheduler)scheduler);
                    }
                }
                else if (forceAsync)
                {
                    ThreadPool.QueueUserWorkItem(continuation, state, preferLocal: true);
                }
                else
                {
                    continuation(state);
                }
            }


            internal void Complete(Exception? ex = null)
            {
                this.exception = ex;
                var localToken = token;
                lock (this)
                {
                    Debug.Assert(_initialized);
                    Debug.Assert(!_isComplete);



                    // Mark operation as completed


                    var previousContinuation = Interlocked.CompareExchange(ref this.continuation, CallbackCompleted, null);

                    Debug.Assert(localToken == token);
                    _isComplete = true;
                    if (previousContinuation != null)
                    {
                        // Async work completed, continue with... continuation
                        ExecutionContext? ec = executionContext;
                        if (ec == null)
                        {
                            InvokeContinuation(previousContinuation, this.state, forceAsync: false);
                        }
                        else
                        {
                            // This case should be relatively rare, as the async Task/ValueTask method builders
                            // use the awaiter's UnsafeOnCompleted, so this will only happen with code that
                            // explicitly uses the awaiter's OnCompleted instead.
                            executionContext = null;
                            ExecutionContext.Run(ec, runState =>
                            {
                                var t = (Tuple<OperationWriter, Action<object?>?, object?>)runState!;
                                t.Item1.InvokeContinuation(t.Item2, t.Item3, forceAsync: false);
                            }, Tuple.Create(this, previousContinuation, this.state));
                        }
                    }
                }

            }

            public void Reset()
            {
                lock (this)
                {

                    Debug.Assert(Volatile.Read(ref _isComplete));
                    _initialized = false;

                    _isComplete = false;
                    this.token++;
                    this.state = null;

                    this.continuation = null;
                    buffer = default;
                    guid = default;
                    type = default;
                    output = default;
                    exception = null;
                    pool.Return(this);
                }

            }

            public void Write()
            {
                Debug.Assert(output != null);
                Debug.Assert(guid != default);
                var length = (int)buffer.Length;
                var span = output.GetMemory(1 + 16 + 4 + length).Span;

                //Write header
                span[0] = (byte)type;
                guid.TryWriteBytes(span.Slice(1, 16));
                BitConverter.TryWriteBytes(span.Slice(17, 4), length);

                //Write body
                //buffer = buffer.Slice(0, length);

                span = span.Slice(21);

                buffer.CopyTo(span);


                //Advance output
                output.Advance(1 + 16 + 4 + length);

            }



            public class OperationWriterPooledObjectPolicy : IPooledObjectPolicy<OperationWriter>
            {



                public OperationWriter Create()
                {
                    return new OperationWriter(_operationWriterPool);
                }

                public bool Return(OperationWriter obj)
                {

                    return true;
                }
            }


        }

        private class HandlerStore
        {
            public HandlerStore(Func<IEnumerable<ITcpPeerConfigurator>> configurators, string id)
            {
                this.configurators = configurators;
                this.id = id;
            }
            public void Add(string operation, Func<TcpPeerRequestContext, Task> handler)
            {
                Handlers.Add(new OperationHandler(operation, handler));
            }
            public List<OperationHandler> Handlers { get; } = new List<OperationHandler>();

            private bool _initialized = false;
            private readonly Func<IEnumerable<ITcpPeerConfigurator>> configurators;
            private readonly string id;

            public bool TryGet(ReadOnlySequence<byte> operation, [NotNullWhen(true)] out OperationHandler? handler)
            {
                if (!_initialized)
                {
                    _initialized = true;
                    var builder = new ApiBuilder(this, id);

                    foreach (var config in configurators())
                    {
                        config.Configure(builder);
                    }
                }

                //We could probably do a lot better, but not sure that !IsSingleSegment is hit often enough to matter?
                ReadOnlySpan<byte> span;
                if (operation.IsSingleSegment)
                {
                    span = operation.FirstSpan;
                }
                else
                {

                    span = new Span<byte>(operation.ToArray());
                }
                for (int i = 0; i < Handlers.Count; i++)
                {
                    var current = Handlers[i];
                    if (UnsafeCompare(span, new ReadOnlySpan<byte>(current.Utf8Representation)) == 0)
                    {
                        handler = current;
                        return true;
                    }
                }
                handler = default;
                return false;
            }

            // Copyright (c) 2008-2013 Hafthor Stefansson
            // Distributed under the MIT/X11 software license
            // Ref: http://www.opensource.org/licenses/mit-license.php.
            static unsafe int UnsafeCompare(ReadOnlySpan<byte> a1, ReadOnlySpan<byte> a2)
            {
                if (a1 == a2) return 0;
                if (a1 == null)
                {
                    return -1;
                }
                else if (a2 == null)
                {
                    return 1;
                }
                else if (a1.Length != a2.Length)
                {
                    return a1.Length - a2.Length;
                }

                fixed (byte* p1 = a1, p2 = a2)
                {
                    byte* x1 = p1, x2 = p2;
                    int l = a1.Length;
                    for (int i = 0; i < l / 8; i++, x1 += 8, x2 += 8)
                    {
                        if (*(long*)x1 != *(long*)x2)
                        {
                            return *(long*)x1 - *(long*)x2 > 0 ? 1 : -1;
                        }
                    }

                    if ((l & 4) != 0)
                    {
                        if (*(int*)x1 != *(int*)x2)
                        {
                            return *(int*)x1 - *(int*)x2;
                        }
                        x1 += 4; x2 += 4;
                    }
                    if ((l & 2) != 0)
                    {
                        if (*(short*)x1 != *(short*)x2)
                        {
                            return *(short*)x1 - *(short*)x2;
                        }
                        x1 += 2; x2 += 2;
                    }

                    if ((l & 1) != 0)
                    {
                        if (*x1 != *x2)
                        {
                            return *x1 - *x2;
                        }
                    }
                    return 0;
                }
            }
        }

        private class MergeDisposable : IDisposable
        {
            private readonly IEnumerable<IDisposable> disposables;

            public MergeDisposable(IEnumerable<IDisposable> disposables)
            {
                this.disposables = disposables;
            }
            public void Dispose()
            {
                foreach (var disposable in disposables)
                {
                    disposable.Dispose();
                }
            }
        }
        private class BroadcastRequestBucket
        {
            public BroadcastRequestBucket(long date)
            {
                Date = date;
            }

            public long Date { get; }

            public HashSet<Guid> Requests { get; } = new HashSet<Guid>();
        }

        /// <summary>
        /// Structure describing disconnection reasons
        /// </summary>
        /// <param name="Reason"></param>
        /// <param name="Error"></param>
        public record DisconnectionReason(string Reason, Exception? Error);

        /// <summary>
        /// keep broadcast requests for 10s.
        /// </summary>
        private const int BUCKETS_COUNT = 10;
        private BroadcastRequestBucket[] _broadcastRequestBuckets = new BroadcastRequestBucket[BUCKETS_COUNT];
        private readonly Meter _meter;
        private readonly Counter<int> _received;
        private readonly Counter<int> _sent;
        private readonly Histogram<double> _processingTime;
        private readonly Stopwatch _watch = new Stopwatch();
        private bool TryAddRequestToBucket(DateTime date, Guid guid)
        {
            lock (_broadcastRequestBuckets)
            {
                var seconds = date.Ticks / TimeSpan.TicksPerSecond;
                var bucket = _broadcastRequestBuckets[seconds % BUCKETS_COUNT];

                if (bucket == null || bucket.Date != seconds)
                {
                    bucket = new BroadcastRequestBucket(seconds);
                    _broadcastRequestBuckets[seconds % BUCKETS_COUNT] = bucket;
                }

                if (bucket.Requests.Contains(guid))
                {
                    return false;
                }
                else
                {
                    bucket.Requests.Add(guid);
                    return true;
                }
            }
        }

        private readonly HandlerStore _handlers;

        private static readonly DefaultObjectPool<TcpRequest> _responsesPool = new DefaultObjectPool<TcpRequest>(new ResponsePooledObjectPolicy(), 10000);
        private static readonly DefaultObjectPool<OperationWriter> _operationWriterPool = new DefaultObjectPool<OperationWriter>(new OperationWriter.OperationWriterPooledObjectPolicy(), 10000);
        private readonly DefaultObjectPool<TcpPeerRequestContext> _requestPool;


        private readonly ConcurrentDictionary<IPEndPoint, RemotePeer> _remoteSockets = new ConcurrentDictionary<IPEndPoint, RemotePeer>();
        private readonly Action<string, Exception> logger;
        private readonly Func<IEnumerable<ITcpPeerConfigurator>> configurators;
        private readonly Action<TcpRequest> onSendingRequest;
        private readonly Func<TcpPeerRequestContext, Task> onRequestStarted;
        private readonly Action<RemotePeer> connected;
        private readonly Action<RemotePeer, DisconnectionReason> onDisconnected;

        private Dictionary<IPEndPoint, Socket> _serverSockets = new Dictionary<IPEndPoint, Socket>();

        internal const string BROADCAST_OPERATION = "#broadcast";


        /// <summary>
        /// Local id of the <see cref="TcpPeer"/> instance, used for logging purpose.
        /// </summary>
        public string Id { get; } = "peer";

        private Stopwatch stopwatch = new Stopwatch();
        /// <summary>
        /// Creates a new <see cref="TcpPeer"/> instance.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="logger"></param>
        /// <param name="configurators"></param>
        /// <param name="onSendingRequest"></param>
        /// <param name="onRequestReceived"></param>
        /// <param name="connected"></param>
        /// <param name="onDisconnected"></param>
        public TcpPeer(string id, Action<string, Exception> logger,
            Func<IEnumerable<ITcpPeerConfigurator>> configurators,
            Action<TcpRequest> onSendingRequest,
            Func<TcpPeerRequestContext, Task> onRequestReceived,
            Action<RemotePeer> connected,
            Action<RemotePeer, DisconnectionReason> onDisconnected)
        {
            _requestPool = new DefaultObjectPool<TcpPeerRequestContext>(new RequestPooledObjectPolicy(this), 10000);
            stopwatch.Start();
            Id = id;
            this.logger = logger;
            this.configurators = configurators;
            this.onSendingRequest = onSendingRequest;
            this.onRequestStarted = onRequestReceived;
            this.connected = connected;
            this.onDisconnected = onDisconnected;

            Task.Run(RunSendQueue);
            _handlers = new HandlerStore(configurators, Id);
            ConfigureInternalHandlers();
            _meter = new Meter($"Stormancer.Tcp.{Id}", "1.0.0");
            _received = _meter.CreateCounter<int>("received");
            _sent = _meter.CreateCounter<int>("sent");

            _processingTime = _meter.CreateHistogram<double>("stormancer.tcp.requestProcessingTime");
            _watch.Start();
            _metadata = new PeerMetadata();
        }

        private void ConfigureInternalHandlers()
        {
            _handlers.Add(BROADCAST_OPERATION, HandleBroadcast);
        }

        private const ushort Prefix6to4 = 0x2002;

        public void SetMetadata(ReadOnlySpan<byte> metadata)
        {

            var owner = _memPool.Rent(metadata.Length);
            metadata.CopyTo(owner.Memory.Span);
            _metadata = new PeerMetadata(_metadata.PeerId, owner, owner.Memory.Slice(0, metadata.Length));
        }

        private PeerMetadata _metadata;


        private static void WriteBroadcastResponseHeader(IBufferWriter<byte> writer, IPEndPoint endpoint, ushort length)
        {
            var span = writer.GetSpan(20);
            // IP Address | port | length
            //     16     |  2   |   2
            //  https://en.wikipedia.org/wiki/6to4

            if (endpoint.AddressFamily == AddressFamily.InterNetwork)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(span, Prefix6to4);

                endpoint.Address.TryWriteBytes(span.Slice(2), out _);
            }
            else
            {
                endpoint.Address.TryWriteBytes(span, out _);
            }

            BinaryPrimitives.TryWriteUInt16BigEndian(span.Slice(16), (ushort)endpoint.Port);
            BinaryPrimitives.TryWriteUInt16BigEndian(span.Slice(18), length);

            writer.Advance(20);
        }

        private static ValueTask<BroadcastResponseHeader> ReadBroadcastResponseHeader(PipeReader reader, CancellationToken cancellationToken)
        {
            static BroadcastResponseHeader ReadResponseHeaderFromResult(ReadResult result, PipeReader pipeReader)
            {
                // IP Address | port | length
                //     16     |  2   |   2
                //  https://en.wikipedia.org/wiki/6to4

                if (result.IsCanceled || result.Buffer.Length < 20)
                {
                    return new BroadcastResponseHeader(false, null, 0);
                }
                var reader = new SequenceReader<byte>(result.Buffer.Slice(0, 20));
                Span<byte> ipBin = stackalloc byte[16];
                reader.TryCopyTo(ipBin);

                IPAddress iPAddress;
                if (MemoryMarshal.Cast<byte, ushort>(ipBin)[0] == Prefix6to4)
                {
                    //IPV4
                    iPAddress = new IPAddress(ipBin.Slice(2, 4));
                }
                else
                {
                    //IPV6
                    iPAddress = new IPAddress(ipBin);
                }
                reader.Advance(16);
                reader.TryReadBigEndian(out short port);
                reader.TryReadBigEndian(out short length);

                pipeReader.AdvanceTo(result.Buffer.GetPosition(20));

                return new BroadcastResponseHeader(true, new IPEndPoint(iPAddress, (ushort)port), (ushort)length);

            }

            var readResultTask = reader.ReadAtLeastAsync(20, cancellationToken);

            if (readResultTask.IsCompleted)
            {
                return new ValueTask<BroadcastResponseHeader>(ReadResponseHeaderFromResult(readResultTask.Result, reader));
            }
            else
            {
                return WaitForHeader(readResultTask, reader);
            }
            static async ValueTask<BroadcastResponseHeader> WaitForHeader(ValueTask<ReadResult> task, PipeReader reader)
            {
                return ReadResponseHeaderFromResult(await task, reader);
            }



        }

        private static void WriteDelimiter(PipeWriter writer, bool value)
        {
            var memory = writer.GetMemory(1);
            memory.Span[0] = (byte)(value ? 1 : 0);
            writer.Advance(1);
        }

        //private static Dictionary<string, List<string>> _debug = new();

        private async Task HandleBroadcast(TcpPeerRequestContext arg)
        {
            //Debug.WriteLine($"starting processing broadcast {arg.Id}");
            async static Task ReadOutResponse(PipeReader reader, Guid currentId, Guid requestId, Channel<(IPEndPoint, IMemoryOwner<byte>, ushort)> channel, CancellationToken cancellationToken)
            {
                //Debug.WriteLine($"{currentId}: start read response from {requestId}");
                try
                {

                    do
                    {
                        var header = await ReadBroadcastResponseHeader(reader, cancellationToken);
                        if (!header.Success)
                        {
                            break;
                        }
                        var result = await reader.ReadAtLeastAsync(header.Length, cancellationToken);



                        var seq = result.Buffer.Slice(0, header.Length);

                        var memOwner = MemoryPool<byte>.Shared.Rent(header.Length);
                        seq.CopyTo(memOwner.Memory.Span);
                        reader.AdvanceTo(seq.End);
                        Debug.Assert(header.Success);
                        channel.Writer.TryWrite((header.Origin!, memOwner, header.Length));





                    }
                    while (true);

                    reader.Complete();

                }
                catch (Exception ex)
                {
                    reader.Complete(ex);
                }
                //Debug.WriteLine($"{currentId}: complete read response from {requestId}");
            }

            async static Task ReadLocalResponse(TcpPeerRequestContext ctx, Guid currentId, Channel<(IPEndPoint, IMemoryOwner<byte>, ushort)> channel, CancellationToken cancellationToken)
            {
                //Debug.WriteLine($"{currentId}: start read local response");
                ReadResult result;
                try
                {
                    do
                    {
                        result = await ctx.ResponsePipe.Reader.ReadAsync(cancellationToken);

                        var remaining = result.Buffer.Length;
                        while (remaining > 0)
                        {
                            ushort length = remaining <= ushort.MaxValue ? (ushort)remaining : ushort.MaxValue;

                            var memOwner = MemoryPool<byte>.Shared.Rent(length);
                            result.Buffer.CopyTo(memOwner.Memory.Span);
                            ctx.ResponsePipe.Reader.AdvanceTo(result.Buffer.End);
                            channel.Writer.TryWrite((ctx.LocalEndpoint, memOwner, length));
                            remaining -= length;
                        }


                    }
                    while (!result.IsCanceled && !result.IsCompleted);

                    ctx.SetOutputReadCompleted();
                }
                catch (Exception ex)
                {
                    ctx.SetOutputReadCompleted(ex);
                }
                //Debug.WriteLine($"{currentId}: comple read local response");
            }

            //var header = await serializer.DeserializeAsync<BroadcastRequestHeader>(arg.Reader, arg.CancellationToken);

            BroadcastRequestHeader header;
            bool success = false;
            do
            {
                var r = await arg.Reader.ReadAsync(arg.CancellationToken);
                if (TryReadBroadcastRequestHeader(r.Buffer, out header, out int consumed))
                {
                    success = true;
                    arg.Reader.AdvanceTo(r.Buffer.GetPosition(consumed));
                }
                else
                {
                    if (r.IsCanceled || r.IsCompleted)
                    {
                        return;
                    }

                    arg.Reader.AdvanceTo(r.Buffer.Start, r.Buffer.End);

                }
            }
            while (!success);

            //Debug.WriteLine($"Received broadcast request {header.Id} {header.Operation}");
            //Request did not reach this node yet.
            if (this.TryAddRequestToBucket(header.Date, header.Id))
            {
                //Debug.WriteLine($"Broadcast request validated {header.Id} {header.Operation}");
                var writers = new List<PipeWriter>();
                var channel = Channel.CreateUnbounded<(IPEndPoint, IMemoryOwner<byte>, ushort)>();
                var remoteEndpoints = ConnectedPeers.Where(ip => !ip.Endpoint.Equals(arg.RemoteEndpoint));

                var outRequests = remoteEndpoints.Select(ip => Send(ip.Endpoint, BROADCAST_OPERATION, arg.CancellationToken)).ToList();
                //Debug.WriteLine($"{arg.Id}: Sending out request to {string.Join(',', outRequests.Select(r => $"{r.Id}:{r.RemotePeer.Endpoint}"))}");
                //if (remoteEndpoints.Any())
                //{
                //    Debug.WriteLine($"Broadcast request redirected to {header.Id} {header.Operation} : {string.Join(',', remoteEndpoints.Select(e => e.Endpoint.ToString()))}");
                //}
                using var disposable = new MergeDisposable(outRequests);

                var tasks = new List<Task>();

                //Reads responses from remote peers. Interleaved from peers connected.


                var ctx = _requestPool.Get();



                async Task WriteResponses(PipeWriter writer, CancellationToken cancellationToken)
                {
                    //Debug.WriteLine($"{arg.Id}: Channel start");
                    while (await channel.Reader.WaitToReadAsync(cancellationToken))
                    {
                        while (channel.Reader.TryRead(out var result))
                        {
                            var (ip, memOwner, length) = result;
                            using (memOwner)
                            {
                                WriteBroadcastResponseHeader(writer, ip, length);

                                writer.Write(memOwner.Memory.Slice(0, length).Span);
                            }
                        }
                        await writer.FlushAsync();
                    }

                    await writer.FlushAsync();

                    //Debug.WriteLine($"{arg.Id}: Channel complete");
                    writer.Complete();


                }

                foreach (var outRequest in outRequests)
                {
                    WriteBroadcastRequestHeader(outRequest.Writer, header.Id, header.Operation, header.Origin, header.Date, header.Metadata);

                    writers.Add(outRequest.Writer);
                    var source = CancellationTokenSource.CreateLinkedTokenSource(arg.CancellationToken);
                    tasks.Add(ReadOutResponse(outRequest.Reader, arg.Id, outRequest.Id, channel, source.Token));
                }


                if (_handlers.TryGet(GetStringBuffer(header.Operation), out var handler))
                {

                    ctx.Init(header.Id, _requestPool, handler, new RemotePeer(null, null, header.Metadata, false), header.Origin, arg.LocalEndpoint, arg.CancellationToken);
                    writers.Add(ctx.RequestPipe.Writer);

                    static async Task RunHandler(OperationHandler h, TcpPeerRequestContext c, TcpPeer peer)
                    {
                        try
                        {
                            await (peer.onRequestStarted?.Invoke(c) ?? Task.CompletedTask);
                            await h.Handler(c);
                            await c.Writer.FlushAsync();
                            c.SetCompleted();
                        }
                        catch (Exception ex)
                        {
                            c.SetCompleted(new RequestException(500, ex));
                        }
                        finally
                        {
                            c.RemotePeer.Metadata.Dispose();
                        }

                    }

                    tasks.Add(RunHandler(handler, ctx, this));
                    tasks.Add(ReadLocalResponse(ctx, arg.Id, channel, arg.CancellationToken));
                }

                var writeResponsesTask = WriteResponses(arg.Writer, arg.CancellationToken);
                var t = arg.Reader.CopyToAsync(writers.ToArray(), arg.CancellationToken, closeOnEnd: true);


                await Task.WhenAll(tasks);
                channel.Writer.Complete();
                await t;
                await writeResponsesTask;

                foreach (var outRq in outRequests)
                {
                    outRq.Dispose();
                }

                //Signals local request completed input write.
                ctx.SetInputWriteCompleted();

            }
            else
            {
                //Debug.WriteLine($"{arg.Id} Already processed broadcast");



            }

            await arg.Writer.FlushAsync();
            arg.Reader.Complete();
            arg.Writer.Complete();
            //Debug.WriteLine($"Completing processing broadcast {arg.Id}");

        }



        /// <summary>
        /// Currently connected remote peers.
        /// </summary>
        public IEnumerable<RemotePeer> ConnectedPeers => _remoteSockets.Values;

        private bool IsAlreadyBound(IPEndPoint endpoint)
        {
            if (endpoint.Address.AddressFamily == AddressFamily.InterNetwork && _isAnyIpv4Bound)
            {
                return true;
            }
            else if (endpoint.Address.AddressFamily == AddressFamily.InterNetworkV6 && _isAnyIpv6Bound)
            {
                return true;
            }
            else
            {
                return _remoteSockets.ContainsKey(endpoint);
            }
        }

        bool _isAnyIpv4Bound = false;
        bool _isAnyIpv6Bound = false;

        /// <summary>
        /// Starts as server.
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public Task RunAsync(IPEndPoint endpoint, CancellationToken ct)
        {

            if (IsAlreadyBound(endpoint))
            {
                throw new InvalidOperationException($"Endpoint {endpoint} already bound.");
            }


            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            ct.Register(() =>
            {
                socket.Close();
            });


            socket.Bind(endpoint);
            _serverSockets[(IPEndPoint)socket.LocalEndPoint!] = socket;
            _isAnyIpv4Bound = endpoint.Address == IPAddress.Any;
            _isAnyIpv6Bound = endpoint.Address == IPAddress.IPv6Any;

            socket.Listen(1000);

            return AcceptConnectionsAsync(socket);
        }

        /// <summary>
        /// Shuts down the server.
        /// </summary>
        public void ShutdownServer()
        {
            if (!_serverSockets.Any())
            {
                return;
            }
            else
            {
                foreach (var socket in _serverSockets.Values)
                {
                    socket.Close();

                }

                _serverSockets.Clear();
            }
        }

        public class ConnectionResult
        {

            internal ConnectionResult(bool success, string reason)
            {
                Success = success;
                Reason = reason;
            }
            public bool Success { get; }
            public string Reason { get; }

            public const string CONNECT_FAILURE_SELF = "connectToSelf";
        }

        /// <summary>
        /// Connects the current <see cref="TcpPeer"/> instance to a server.
        /// </summary>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        public async Task<ConnectionResult> Connect(IPEndPoint endpoint)
        {


            var connection = await SocketConnection.ConnectAsync(endpoint, null);
            var tcs = new TaskCompletionSource();
            var disconnectionReason = new DisconnectionReason(string.Empty, null);
            async Task ListenSocket(SocketConnection c)
            {
                RemotePeer? peer = null;
                try
                {

                    peer = await EstablishConnection(c, c.Socket, true, CancellationToken.None);

                    if (peer != null)
                    {
                        tcs.SetResult();
                        await ReadPipeAsync(peer, c.Socket, false, CancellationToken.None);
                        disconnectionReason = new DisconnectionReason("socketClosed", null);
                    }
                    else
                    {
                        disconnectionReason = new DisconnectionReason(ConnectionResult.CONNECT_FAILURE_SELF, null);
                        tcs.SetResult();
                    }
                }
                catch (Exception ex)
                {
                    disconnectionReason = new DisconnectionReason("socketError", ex);
                }
                finally
                {

                    if (peer != null)
                    {
                        Cleanup(peer, disconnectionReason);
                    }
                    else
                    {
                        tcs.TrySetException(new InvalidOperationException($"connection to {endpoint} failed"));
                    }
                }
            }

            _ = ListenSocket(connection);
            await tcs.Task;
            return new ConnectionResult(disconnectionReason.Reason == string.Empty, disconnectionReason.Reason);

        }

        /// <summary>
        /// Disconnects the peer from a remote socket.
        /// </summary>
        /// <param name="endpoint"></param>
        public void Disconnect(IPEndPoint endpoint)
        {
            if (_remoteSockets.TryGetValue(endpoint, out var peer))
            {
                peer.Connection.Socket.Close();
            }
        }

        private async Task RunSendQueue()
        {

            var channelReader = _operationsChannel.Reader;
            HashSet<PipeWriter> outputs = new HashSet<PipeWriter>();
            while (await channelReader.WaitToReadAsync())
            {
                try
                {
                    outputs.Clear();

                    while (channelReader.TryRead(out var op))
                    {

                        Debug.Assert(op.output != null);
                        try
                        {

                            op.Write();
                            outputs.Add(op.output);
                            //Debug.WriteLine($"{stopwatch.Elapsed} Sent {op.guid} {Enum.GetName(op.type)}");


                        }
                        catch (Exception)
                        {

                        }
                        finally
                        {
                            if (op.Disposable is not null)
                            {
                                op.Disposable.Dispose();
                            }
                        }
                    }

                    foreach (var output in outputs)
                    {
                        _ = output.FlushAsync();
                    }

                }
                catch (Exception ex)
                {
                    logger?.Invoke("An error occured while processing the send queue.", ex);
                }

            }

        }

        private Channel<Operation> _operationsChannel = Channel.CreateUnbounded<Operation>();



        private void EnqueueOperation(Operation op)
        {

            if (!_operationsChannel.Writer.TryWrite(op))
            {
                Debug.Assert(false);
            }


        }


        private class PipePooledObjectPolicy : IPooledObjectPolicy<Pipe>
        {
            private static PipeOptions _pipeOptions = new PipeOptions(MemoryPool<byte>.Shared);



            public Pipe Create()
            {

                return new Pipe(_pipeOptions);
            }

            public bool Return(Pipe obj)
            {

                obj.Reset();

                return true;
            }
        }
        private class ResponsePooledObjectPolicy : IPooledObjectPolicy<TcpRequest>
        {
            public TcpRequest Create()
            {
                return new TcpRequest();
            }

            public bool Return(TcpRequest obj)
            {
                obj.Reset();
                return true;
            }
        }
        private class RequestPooledObjectPolicy : IPooledObjectPolicy<TcpPeerRequestContext>
        {
            private readonly TcpPeer peer;

            public RequestPooledObjectPolicy(TcpPeer peer)
            {
                this.peer = peer;
            }
            public TcpPeerRequestContext Create()
            {
                return new TcpPeerRequestContext(peer);
            }

            public bool Return(TcpPeerRequestContext obj)
            {
                obj.Reset();
                return true;
            }
        }


        private MemoryPool<byte> _memPool = MemoryPool<byte>.Shared;
        private ConcurrentDictionary<string, Lazy<byte[]>> _operationsCache = new ConcurrentDictionary<string, Lazy<byte[]>>();

        private ReadOnlySequence<byte> GetStringBuffer(string operation) => new ReadOnlySequence<byte>(_operationsCache.GetOrAdd(operation, o => new Lazy<byte[]>(() =>
        {
            var length = Encoding.UTF8.GetByteCount(o);
            byte[] data = new byte[length + 1];

            if (length > byte.MaxValue)
            {
                throw new NotSupportedException($"Operation name '{o}' too long. max length is {byte.MaxValue}");
            }
            data[0] = (byte)length;
            Encoding.UTF8.GetBytes(o, data.AsSpan().Slice(1));

            return data;
        })).Value);


        private ConcurrentDictionary<int, Lazy<byte[]>> _codeCache = new ConcurrentDictionary<int, Lazy<byte[]>>();
        private ReadOnlySequence<byte> GetCodeBuffer(int code) => new ReadOnlySequence<byte>(_codeCache.GetOrAdd(code, o => new Lazy<byte[]>(() => BitConverter.GetBytes(o))).Value);

        private ValueTask ReadSendPipe(TcpRequest request, CancellationToken cancellationToken)
        {
            Debug.Assert(request.sendPipe != null && request.RemotePeer != null);

            var sendPipe = request.sendPipe;
            var socket = request.RemotePeer.Connection;


            var t = ReadSendPipe(sendPipe, socket, request.Operation, request.Id, false, cancellationToken);
            if (t.IsCompletedSuccessfully)
            {
                request.SetInputCompleted();
                return t;
            }
            else if (t.IsFaulted)
            {
                try
                {
                    t.AsTask().Wait();
                }
                catch (Exception ex)
                {

                    logger?.Invoke($"an error occured while processing request inputs '{request.Operation}'. (1994)", ex);
                    var forwardedException = new RequestException(TcpRequestErrorCodes.OperationError, ex);
                    request.SetInputCompleted(forwardedException);
                    request.SetOutputCompleted(forwardedException);
                }


                return t;
            }
            else
            {
                static async ValueTask WaitAndCompleteAsync(ValueTask t, TcpRequest request, Action<string, Exception> logger)
                {
                    try
                    {
                        await t;
                        request.SetInputCompleted();
                    }
                    catch (Exception ex)
                    {
                        logger?.Invoke($"an error occured while processing the input channel of a request '{request.Operation}'. (2014)", ex);
                        var forwardedException = new RequestException(TcpRequestErrorCodes.OperationError);
                        request.SetInputCompleted(forwardedException);
                        request.SetOutputCompleted(forwardedException);
                    }

                }
                return WaitAndCompleteAsync(t, request, logger);
            }


        }

        private async Task ReadSendPipe(TcpPeerRequestContext ctx)
        {
            Debug.Assert(ctx.ResponsePipe != null && ctx.RemotePeer != null);
            var id = ctx.Id;
            var sendPipe = ctx.ResponsePipe;
            var socket = ctx.RemotePeer.Connection;

            try
            {
                await ReadSendPipe(sendPipe, socket, ctx.Operation, ctx.Id, true, ctx.CancellationToken);
            }
            finally
            {
                ctx.SetOutputReadCompleted();
            }
        }

        private void SendCancel(Guid id, PipeWriter output)
        {
            var op = new Operation();

            op.output = output;

            op.buffer = GetCodeBuffer(TcpRequestErrorCodes.OperationCanceled);
            op.guid = id;
            op.type = MessageType.Canceled;

            EnqueueOperation(op);
        }

        private ValueTask ReadSendPipe(Pipe sendPipe, SocketConnection socket, string operation, Guid id, bool isResponse = false, CancellationToken cancellationToken = default)
        {
            PipeReader reader = sendPipe.Reader;
            var operationBuffer = GetStringBuffer(operation);
            ReadOnlySequence<byte> buffer;
            var output = socket.Output;


            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask();
            }

            //Do not send "begin" message in responses.
            bool beginSent = isResponse;
            bool endSent = false;


            static void ProcessReadResult(string operation, ref ReadResult result, ref bool beginSent, ref bool endSent, bool isResponse, Guid id, TcpPeer peer, PipeWriter output)
            {
                if (endSent)
                {
                    return;
                }

                var buffer = result.Buffer;
                var isCompleted = result.IsCompleted;

                var length = buffer.Length;

                IMemoryOwner<byte> memOwner;

                if (!beginSent)
                {
                    var operationBuffer = peer.GetStringBuffer(operation);
                    length += operationBuffer.Length;
                    memOwner = MemoryPool<byte>.Shared.Rent((int)length);

                    operationBuffer.CopyTo(memOwner.Memory.Span);
                    buffer.CopyTo(memOwner.Memory.Span.Slice((int)operationBuffer.Length));
                }
                else
                {
                    memOwner = MemoryPool<byte>.Shared.Rent((int)length);
                    buffer.CopyTo(memOwner.Memory.Span);
                }

                var op = new Operation();

                var opType = (isResponse ? MessageType.Response : MessageType.Request)
                    | (beginSent ? MessageType.Empty : MessageType.Begin)
                    | (buffer.Length > 0 ? MessageType.Content : MessageType.Empty)
                    | (isCompleted ? MessageType.End : MessageType.Empty);

                op.output = output;
                op.buffer = new ReadOnlySequence<byte>(memOwner.Memory.Slice(0, (int)length));
                op.guid = id;
                op.type = opType;
                op.Disposable = memOwner;

                peer.EnqueueOperation(op);

                //Debug.WriteLine($"sent msg for rq {id}, isResponse={isResponse}, opType={Convert.ToString((byte)opType, 2).PadLeft(8, '0')}");
                beginSent = true;

                if (isCompleted)
                {
                    endSent = true;
                }

            }
            static bool TryProcessPipe(string operation, PipeReader reader, ref bool beginSent, ref bool endSent, bool isResponse, Guid id, TcpPeer peer, PipeWriter output)
            {
                if (!endSent && reader.TryRead(out var result))
                {
                    ProcessReadResult(operation, ref result, ref beginSent, ref endSent, isResponse, id, peer, output);
                    reader.AdvanceTo(result.Buffer.End);
                    return true;
                }
                else
                {

                    return false;
                }
            }

            static async ValueTask ProcessPipeAsync(string operation, PipeReader reader, bool beginSent, bool endSent, bool isResponse, Guid id, TcpPeer peer, PipeWriter output, CancellationToken cancellationToken)
            {
                try
                {
                    using var sub = cancellationToken.Register((state) => ((PipeReader)state!).CancelPendingRead(), reader);
                    while (!cancellationToken.IsCancellationRequested && !endSent)
                    {
                        var result = await reader.ReadAsync();
                        if (result.IsCanceled)
                        {
                            endSent = true;
                            return;
                        }
                        reader.AdvanceTo(result.Buffer.Start);
                        while (TryProcessPipe(operation, reader, ref beginSent, ref endSent, isResponse, id, peer, output))
                        {

                        }

                    }

                }
                catch (RequestException ex) when (ex.Code != TcpRequestErrorCodes.ConnectionLost)
                {

                    if (!endSent)
                    {
                        endSent = true;
                        //buffer = ReadOnlySequence<byte>.Empty;
                        peer.EnqueueError(output, id, ex.Code);
                    }

                    //if (operation == "s2s.rq") Debug.WriteLine($"Sent {op.type} for {id}");
                }
                catch (Exception ex)
                {
                    if (!endSent)
                    {
                        endSent = true;
                        peer.logger?.Invoke("An unexpected error occured while processing a send queue", ex);

                        //buffer = ReadOnlySequence<byte>.Empty;
                        peer.EnqueueError(output, id, 500);
                    }
                    throw;
                }
                finally
                {
                    if (cancellationToken.IsCancellationRequested && !endSent)
                    {
                        endSent = true;
                        peer.EnqueueCancel(output, id);
                    }


                    Debug.Assert(endSent);
                }
            }



            try
            {


                while (TryProcessPipe(operation, reader, ref beginSent, ref endSent, isResponse, id, this, output))
                {

                }

                if (!endSent)
                {
                    return ProcessPipeAsync(operation, reader, beginSent, endSent, isResponse, id, this, output, cancellationToken);
                }


            }
            catch (RequestException ex) when (ex.Code != TcpRequestErrorCodes.ConnectionLost)
            {

                if (!endSent)
                {
                    endSent = true;
                    //buffer = ReadOnlySequence<byte>.Empty;
                    EnqueueError(output, id, ex.Code);
                }

                //if (operation == "s2s.rq") Debug.WriteLine($"Sent {op.type} for {id}");
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested && !endSent)
                {
                    endSent = true;
                    EnqueueCancel(output, id);
                }

            }
            Debug.Assert(endSent);
            return new ValueTask();



        }

        private void EnqueueError(PipeWriter output, Guid id, int code)
        {
            var op = new Operation();

            op.output = output;

            op.buffer = GetCodeBuffer(code);
            op.guid = id;
            op.type = MessageType.Error;

            //if (operation == "s2s.rq") Debug.WriteLine($"Sending {op.type} for {id}");
            EnqueueOperation(op);
        }

        private void EnqueueCancel(PipeWriter? output, Guid id)
        {
            var op = new Operation();

            op.output = output;

            op.buffer = ReadOnlySequence<byte>.Empty;
            op.guid = id;
            op.type = MessageType.Canceled;

            //if (operation == "s2s.rq") Debug.WriteLine($"Sending {op.type} for {id}");
            EnqueueOperation(op);
        }

        private IPEndPoint? GetLocalEndpointForRemote(IPEndPoint remoteEndpoint)
        {
            return _serverSockets.Keys.FirstOrDefault(endpoint => endpoint.AddressFamily == remoteEndpoint.AddressFamily);
        }

        /// <summary>
        /// Gets the endpoint the local socket is associated to.
        /// </summary>
        public IEnumerable<IPEndPoint> LocalEndpoints => _serverSockets.Values.Select(s => (IPEndPoint)s.LocalEndPoint!); //If we let the OS choose a porte, the key might contain a 0 as port but the socket itself contains the real port.


    
        /// <summary>
        /// Sends a request on the <see cref="TcpPeer"/> instance.
        /// </summary>
        /// <param name="endpoint">Endpoint to send the request to. Use null to send to self.</param>
        /// <param name="operation"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public TcpRequest Send(IPEndPoint? endpoint, string operation, CancellationToken cancellationToken)
        {
            _sent.Add(1);
            var id = Guid.NewGuid();

            if (endpoint == null || _serverSockets.Keys.Any(e => e.Equals(endpoint)))
            {
                endpoint = _serverSockets.Keys.First();
                //pendingResponses.
                var response = new TcpRequest(); //_responsesPool.Get();
                var ctx = _requestPool.Get();

                var sub = cancellationToken.Register(() =>
                {
                    if (TryRemoveFromPendingRequests(id, out var r, "1849"))
                    {
                        //Debug.WriteLine($"{stopwatch.Elapsed}, Removed tcpPeer 1848 {id}");
                        r.SetOutputCompleted();
                        r.SetInputCompleted();
                        ctx.Cancel();
                    }
                });
                response.Init(id, operation/*, _responsesPool*/, null, () =>
                {
                    //Debug.WriteLine($"Dispose {id}");
                    sub.Dispose();
                    if (cancellationToken.IsCancellationRequested && TryRemoveFromPendingRequests(id, out var r, "1861"))
                    {
                        //Debug.WriteLine($"{stopwatch.Elapsed}, Removed tcpPeer 1862 {id}");
                        r.SetOutputCompleted();
                        r.SetInputCompleted();
                        if (r.RemotePeer != null && r.RemotePeer.Connection != null)
                        {

                            SendCancel(r.Id, r.RemotePeer.Connection.Output);
                        }
                    }
                });

                onSendingRequest?.Invoke(response);

                if (TryAddToPendingRequests(response))
                {
                    //Debug.WriteLine($"{stopwatch.Elapsed} Added request {response.Id}. 1875");
                }


                if (_handlers.TryGet(GetStringBuffer(operation), out var handler))
                {



                    var localEndpoint = GetLocalEndpointForRemote(endpoint);
                    if (localEndpoint == null)
                    {
                        throw new InvalidOperationException($"Cannot send message to {endpoint} : No compatible local socket bound.");
                    }

                    ctx.Init(id, _requestPool, handler, new RemotePeer(null, null, _metadata, false), localEndpoint, localEndpoint, cancellationToken);
                    //Tests: Console.Writeline($"Sending {id} operation {response.Operation}");
                    SendLocalImpl(ctx, response);
                }
                else
                {
                    var ex = new RequestException(TcpRequestErrorCodes.OperationNotFound);
                    response.SetInputCompleted(ex);
                    response.SetOutputCompleted(ex);
                }

                return response;

            }
            else if (_remoteSockets.TryGetValue(endpoint, out var socket))
            {


                //pendingResponses.
                var response = new TcpRequest(); //_responsesPool.Get();

                var sub = cancellationToken.Register(() =>
                {
                    //Debug.WriteLine($"Cancel {id}");
                    if (TryRemoveFromPendingRequests(id, out var r, "1914"))
                    {
                        //Debug.WriteLine($"{stopwatch.Elapsed}, Removed tcpPeer 1911 {id}");
                        r.SetOutputCompleted();
                        r.SetInputCompleted();
                        SendCancel(r.Id, r.RemotePeer.Connection.Output);
                    }
                });

                response.Init(id, operation,/* _responsesPool,*/ socket, () =>
                {
                    //Debug.WriteLine($"Dispose {id}");
                    sub.Dispose();
                    if (cancellationToken.IsCancellationRequested && TryRemoveFromPendingRequests(id, out var r, "1927"))
                    {
                        //Debug.WriteLine($"{stopwatch.Elapsed}, Removed tcpPeer 1924 {id}");
                        r.SetOutputCompleted();
                        r.SetInputCompleted();
                        SendCancel(r.Id, r.RemotePeer.Connection.Output);
                    }
                });
                if (TryAddToPendingRequests(response))
                {

                    //Debug.WriteLine($"{stopwatch.Elapsed} Added request {response.Id} (1938).");

                }

                onSendingRequest?.Invoke(response);

                //Tests: Console.Writeline($"Sending {id} operation {response.Operation}");
                SendImpl(response, cancellationToken);

                return response;
            }
            else
            {
                throw new InvalidOperationException($"Peer not connected to {endpoint}");
            }
        }


        private static void WriteBroadcastRequestHeader(IBufferWriter<byte> writer, Guid id, string operation, IPEndPoint endpoint, DateTime date, PeerMetadata metadata)
        {
            // IP Address | port | id | date | opLength | op |  metadata
            //     16     |  2   | 16     8         1          
            //  https://en.wikipedia.org/wiki/6to4
            var opLength = Encoding.UTF8.GetByteCount(operation);
            if (opLength > byte.MaxValue)
            {
                throw new InvalidOperationException("Operation cannot be longer than 256");
            }

            var span = writer.GetSpan(43 + opLength);


            if (endpoint.AddressFamily == AddressFamily.InterNetwork)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(span, Prefix6to4);

                endpoint.Address.TryWriteBytes(span.Slice(2), out _);
            }
            else
            {
                endpoint.Address.TryWriteBytes(span, out _);
            }

            BinaryPrimitives.TryWriteUInt16BigEndian(span.Slice(16), (ushort)endpoint.Port);
            id.TryWriteBytes(span.Slice(18));

            BinaryPrimitives.TryWriteInt64BigEndian(span.Slice(34), date.Ticks);
            span[42] = (byte)opLength;


            Encoding.UTF8.GetBytes(operation, span.Slice(43));

            writer.Advance(43 + opLength);

            PeerMetadata.Write(writer, metadata);
        }

        private bool TryReadBroadcastRequestHeader(ReadOnlySequence<byte> buffer, out BroadcastRequestHeader header, out int consumed)
        {
            // IP Address | port | id | date | opLength |  op |metadata
            //     16     |  2   | 16     8         1           
            //  https://en.wikipedia.org/wiki/6to4

            if (buffer.Length < 43)
            {
                header = default;
                consumed = 0;
                return false;
            }
            var opLength = buffer.Slice(42, 1).FirstSpan[0];
            var reader = new SequenceReader<byte>(buffer.Slice(0, 43));


            if (buffer.Length < 43 + opLength)
            {
                header = default;
                consumed = 0;
                return false;
            }





            Span<byte> ipBin = stackalloc byte[16];
            reader.TryCopyTo(ipBin);

            IPAddress iPAddress;
            if (MemoryMarshal.Cast<byte, ushort>(ipBin)[0] == Prefix6to4)
            {
                //IPV4
                iPAddress = new IPAddress(ipBin.Slice(2, 4));
            }
            else
            {
                //IPV6
                iPAddress = new IPAddress(ipBin);
            }
            reader.Advance(16);
            reader.TryReadBigEndian(out short port);
            reader.TryCopyTo(ipBin);
            reader.Advance(16);

            var id = new Guid(ipBin);

            reader.TryReadBigEndian(out long ticks);



            var operation = Encoding.UTF8.GetString(buffer.Slice(43, opLength));
            var success = PeerMetadata.TryRead(buffer.Slice(43 + opLength), out var metadata, out var metadataConsumed);

            if (!success)
            {
                header = default;
                consumed = 0;
                return false;
            }

            header = new BroadcastRequestHeader
            {
                Id = id,
                Date = new DateTime(ticks),
                Origin = new IPEndPoint(iPAddress, (ushort)port),
                Operation = operation,
                Metadata = metadata
            };


            consumed = 43 + opLength + metadataConsumed;
            return true;



        }
        public BroadcastRequest Broadcast(string operation, CancellationToken ct)
        {


            if (LocalEndpoints == null)
            {
                throw new InvalidOperationException("Peer not connected");
            }
            if (!LocalEndpoints.Any())
            {
                throw new InvalidOperationException("Peer must be started as a server to be able to broadcast.");
            }

            var request = new BroadcastRequest();
            request.Init();
            async Task BroadcastImpl()
            {
                using var r = Send(LocalEndpoints.First(), BROADCAST_OPERATION, ct);

                async Task WriteRequest(BroadcastRequest request, TcpRequest r, IPEndPoint localEndpoint, CancellationToken ct)
                {
                    Debug.Assert(request.sendPipe != null);
                    try
                    {
                        //Debug.WriteLine($"starting broadcast {request.Id}");
                        WriteBroadcastRequestHeader(r.Writer, request.Id, operation, localEndpoint, DateTime.UtcNow, _metadata);

                        await request.sendPipe.Reader.CopyToAsync(r.Writer, ct);
                        await r.Writer.FlushAsync();
                        r.Writer.Complete();
                        request.SetReaderComplete();

                    }
                    catch (Exception ex)
                    {
                        r.Writer.Complete(ex);
                        request.SetReaderComplete(ex);
                    }
                }

                async Task ReadResponse(TcpRequest request, BroadcastRequest broadcastRequest, CancellationToken ct)
                {
                    try
                    {

                        ReadResult readResult;
                        do
                        {
                            var header = await ReadBroadcastResponseHeader(request.Reader, ct);
                            if (!header.Success)
                            {
                                break;
                            }

                            var response = broadcastRequest.GetOrAddResponse(header.Origin);
                            //response.Headers.Add(header);
                            Debug.Assert(response.pipe != null);

                            readResult = await request.Reader.ReadAtLeastAsync(header.Length, ct);


                            foreach (var segment in readResult.Buffer.Slice(0, header.Length))
                            {
                                response.pipe.Writer.Write(segment.Span);

                            }
                            request.Reader.AdvanceTo(readResult.Buffer.GetPosition(header.Length));
                            await response.pipe.Writer.FlushAsync();

                        }
                        while (true);

                        request.Reader.Complete();
                        broadcastRequest.SetWritersComplete();

                    }
                    catch (Exception ex)
                    {
                        request.Reader.Complete(ex);
                        broadcastRequest.SetWritersComplete(ex);
                    }
                }

                await Task.WhenAll(
                    WriteRequest(request, r, LocalEndpoints.First(), ct),
                    ReadResponse(r, request, ct));

            }

            _ = BroadcastImpl();
            return request;
        }

        private void SendLocalImpl(TcpPeerRequestContext ctx, TcpRequest request)
        {
            _ = Task.Run(() => ReadSendPipeLocal(ctx, request));
        }

        private async Task ReadSendPipeLocal(TcpPeerRequestContext ctx, TcpRequest request)
        {
            var id = request.Id;
            Debug.Assert(request.sendPipe != null && request.receivePipe != null && ctx.ResponsePipe != null && ctx.RequestPipe != null);
            //Can only run if the peer is server.
            Debug.Assert(ctx.OperationHandler != null);
            try
            {
                async static Task CopyInputAsync(TcpRequest request, TcpPeerRequestContext ctx)
                {
                    var reader = request.sendPipe.Reader;
                    var writer = ctx.RequestPipe.Writer;

                    await using var registration = ctx.CancellationToken.Register(reader.CancelPendingRead);
                    try
                    {
                        ReadResult readResult;
                        do
                        {
                            readResult = await reader.ReadAsync();

                            if (!readResult.IsCanceled)
                            {
                                foreach (var segment in readResult.Buffer)
                                {
                                    writer.Write(segment.Span);
                                }
                                await writer.FlushAsync();
                                reader.AdvanceTo(readResult.Buffer.End);

                            }
                        }
                        while (!readResult.IsCompleted && !readResult.IsCanceled);

                        request.SetInputCompleted();
                        ctx.SetInputWriteCompleted();
                    }
                    catch (Exception ex)
                    {
                        request.SetInputCompleted(ex);
                        ctx.SetInputWriteCompleted(ex);
                    }
                    //Tests: Console.Writeline($"Completed input copy {copied}bytes " + ctx.Id);
                }

                async static Task CopyOutputAsync(Guid id, TcpRequest request, TcpPeerRequestContext ctx)
                {
                    var reader = ctx.ResponsePipe.Reader;
                    var writer = request.receivePipe.Writer;
                    await using var registration = ctx.CancellationToken.Register(reader.CancelPendingRead);
                    try
                    {
                        ReadResult readResult;
                        do
                        {
                            readResult = await reader.ReadAsync();

                            if (!readResult.IsCanceled)
                            {
                                foreach (var segment in readResult.Buffer)
                                {

                                    var flushResult = await writer.WriteAsync(segment);
                                    if (flushResult.IsCanceled)
                                    {
                                        break;
                                    }

                                }
                                reader.AdvanceTo(readResult.Buffer.End);

                            }
                        }
                        while (!readResult.IsCompleted && !readResult.IsCanceled);


                        ctx.SetOutputReadCompleted();

                        request.SetOutputCompleted();
                    }
                    catch (Exception ex)
                    {

                        ctx.SetOutputReadCompleted(ex);
                        request.SetOutputCompleted(ex);
                    }

                    //Tests: Console.Writeline($"Completed output copy {copied}bytes " + ctx.Id);
                }
                var t1 = CopyInputAsync(request, ctx);
                var t2 = CopyOutputAsync(request.Id, request, ctx);

                if (ctx.OperationHandler.Operation != BROADCAST_OPERATION)
                {
                    await onRequestStarted(ctx);
                }
                await ctx.OperationHandler.Handler(ctx);
                await ctx.Writer.FlushAsync();
                //Handling complete, no need to keep writing input data.

                ctx.SetCompleted();
                await Task.WhenAll(t1, t2);
                if (TryRemoveFromPendingRequests(id, out _, "2163"))
                {
                    //Debug.WriteLine($"{stopwatch.Elapsed}, Removed tcpPeer 2155 {id}");
                }


            }
            catch (RequestException ex)
            {
                ctx.SetCompleted(ex);
            }
            catch (Exception ex)
            {
                logger?.Invoke($"an error occured while processing a request '{request.Operation}' (2728)", ex);
                var forwardedException = new RequestException(TcpRequestErrorCodes.OperationError, ex);
                ctx.SetCompleted(forwardedException);

            }

        }

        private void SendImpl(TcpRequest rq, CancellationToken cancellationToken)
        {

            ThreadPool.QueueUserWorkItem<(TcpPeer, TcpRequest, CancellationToken)>((tuple) =>
            {
                var (peer, rq, cancellationToken) = tuple;
                peer.ReadSendPipe(rq, cancellationToken);
            }, (this, rq, cancellationToken), false);



        }

        /// <summary>
        /// Gets or sets a Func executed whenever a new connection is accepted.
        /// </summary>
        public Func<Socket, bool> OnAcceptedConnection { get; set; }

        private async Task AcceptConnectionsAsync(Socket localSocket)
        {
            var localEndpoint = (IPEndPoint?)localSocket.LocalEndPoint;

            while (true)
            {
                try
                {
                    var clientSocket = await localSocket.AcceptAsync();

                    _ = ProcessMessagesAsync(clientSocket, localSocket);



                }
                catch (Exception ex)
                {
                    foreach (var peer in _remoteSockets.Values)
                    {
                        if (peer.LocalSocket == localSocket)
                        {
                            Cleanup(peer, new DisconnectionReason("acceptClosed", ex));
                        }
                    }

                    if (localEndpoint != null)
                    {
                        _serverSockets.Remove(localEndpoint);
                    }

                    break;
                }
            }
        }
        private async Task ProcessMessagesAsync(Socket socket, Socket localSocket)
        {
            Debug.Assert(socket.RemoteEndPoint != null);

            var connection = SocketConnection.Create(socket);
            RemotePeer? peer = null;
            DisconnectionReason disconnectionReason = new DisconnectionReason(string.Empty, null);

            try
            {
                if (OnAcceptedConnection?.Invoke(socket) ?? true)
                {

                    peer = await EstablishConnection(connection, localSocket, false, CancellationToken.None);
                    if (peer != null)
                    {
                        await ReadPipeAsync(peer, localSocket, true, CancellationToken.None);
                        disconnectionReason = new DisconnectionReason("connectionClosed", null);
                    }
                    else
                    {
                        disconnectionReason = new DisconnectionReason("connectToSelf", null);
                    }
                }
                else
                {
                    disconnectionReason = new DisconnectionReason("connectionRefused", null);
                }
            }
            catch (ConnectionResetException ex)
            {
                disconnectionReason = new DisconnectionReason("connectionReset", ex);
            }
            catch (Exception ex)
            {
                disconnectionReason = new DisconnectionReason("error", ex);
                throw;
            }
            finally
            {
                if (peer != null)
                {
                    if (peer.Connection.Socket.Connected)
                    {
                        peer.Connection.Socket.Close();
                    }

                    Cleanup(peer, disconnectionReason);
                }
            }
        }


        private async Task<RemotePeer?> EstablishConnection(SocketConnection connection, Socket? receivingSocket, bool isClient, CancellationToken cancellationToken)
        {
            var reader = connection.Input;
            if (isClient)
            {

                PeerMetadata.Write(connection.Output, _metadata);
                await connection.Output.FlushAsync();
            }

            var metadata = await PeerMetadata.ReadMetadataAsync(reader, cancellationToken);


            if (metadata.PeerId == _metadata.PeerId && isClient)
            {
                connection.Socket.Close();
                return null;
            }


            if (!isClient)
            {
                PeerMetadata.Write(connection.Output, _metadata);
                await connection.Output.FlushAsync();
            }



            var peer = new RemotePeer(connection, receivingSocket, metadata, !isClient);

            var remoteEndpoint = (IPEndPoint?)peer.Connection.Socket.RemoteEndPoint;
            if (remoteEndpoint != null)
            {
                _remoteSockets.TryAdd(remoteEndpoint, peer);
                connected(peer);
            }
            else
            {
                throw new InvalidOperationException("Connection failed.");
            }
            return peer;

        }

        private void Cleanup(RemotePeer peer, DisconnectionReason reason)
        {
            var endpoint = peer.Endpoint;

            foreach (var id in peer.RequestsSentTo)
            {
                if (TryRemoveFromPendingRequests(id.Id, out var rq, "2304"))
                {

                    Debug.Assert(rq.receivePipe != null);
                    var ex = new RequestException(TcpRequestErrorCodes.ConnectionLost);
                    rq.SetInputCompleted(ex);
                    rq.SetOutputCompleted(ex);

                }
            }
            foreach (var id in peer.RequestsReceivedFrom)
            {
                if (TryRemoveFromPendingContexts(id.Id, out var rq, "2316"))
                {
                    var ex = new RequestException(TcpRequestErrorCodes.ConnectionLost);
                    ////Tests: Console.Writeline("Remove:"+id);
                    rq.SetInputWriteCompleted(ex);
                    rq.Cancel();
                }
            }
            peer.Metadata.Dispose();
            Debug.Assert(endpoint != null);
            _remoteSockets.TryRemove(endpoint, out _);
            onDisconnected(peer, reason);
        }
        private async ValueTask ReadPipeAsync(RemotePeer peer, Socket receivingSocket, bool isServer, CancellationToken cancellationToken)
        {


            var reader = peer.Connection.Input;
            Debug.Assert(peer.Connection.Socket.RemoteEndPoint != null);

            //try
            //{
            var message = new Message() { Header = new MessageHeader { Length = -1 } };
            //message.Header.Length = -1;


            message.Origin = peer;

            while (peer.Connection.Socket.Connected)
            {



                ReadResult result = await reader.ReadAsync(cancellationToken);



                do
                {
                    ReadOnlySequence<byte> buffer = result.Buffer;
                    try
                    {
                        // Process all messages from the buffer, modifying the input buffer on each
                        // iteration.

                        while (TryParseMessage(ref buffer, ref message))
                        {
                            ProcessMessageAsync(message, (IPEndPoint)receivingSocket.LocalEndPoint!, peer);

                            message = new Message() { Header = new MessageHeader { Length = -1 } };
                            message.Origin = peer;
                        }


                        // There's no more data to be processed.
                        if (result.IsCompleted)
                        {
                            if (buffer.Length > 0)
                            {
                                // The message is incomplete and there's no more data to process.
                                throw new InvalidDataException("Incomplete message.");
                            }
                            break;
                        }
                    }
                    finally
                    {
                        // Since all messages in the buffer are being processed, you can use the
                        // remaining buffer's Start and End position to determine consumed and examined.
                        reader.AdvanceTo(buffer.Start, buffer.End);
                    }
                }
                while (reader.TryRead(out result));
            }
            //}
            //Not useful because if we get there, it's because the socket is closed.
            //finally
            //{
            //    await reader.CompleteAsync(new RequestException(600));
            //}
        }

        private bool TryParseMessage(ref ReadOnlySequence<byte> buffer, ref Message message)
        {
            if (buffer.Length == 0)
            {
                return false;
            }

            //var reader = new MessagePackReader(buffer);

            if (!message.Header.IsValid)
            {
                if (buffer.Length < 21) //Headers are 21 bytes long, so we know that we don't have enough data, no point in trying to deserialize.
                {
                    return false;
                }

                try
                {

                    var type = buffer.FirstSpan[0];
                    //if (!Enum.IsDefined(typeof(MessageType), type))
                    //{
                    //    throw new InvalidOperationException("Invalid message header.");
                    //}
                    var guidSlice = buffer.Slice(1, 16);
                    Guid id;
                    if (guidSlice.IsSingleSegment || guidSlice.FirstSpan.Length >= 16)
                    {
                        id = new Guid(guidSlice.FirstSpan);
                    }
                    else
                    {

                        id = new Guid(guidSlice.ToArray());
                    }

                    var lengthSlice = buffer.Slice(17, 4);
                    int length;
                    if (lengthSlice.IsSingleSegment || lengthSlice.FirstSpan.Length >= 4)
                    {
                        length = BitConverter.ToInt32(lengthSlice.FirstSpan);
                    }
                    else
                    {
                        length = BitConverter.ToInt32(lengthSlice.ToArray());
                    }


                    message.Header = new MessageHeader
                    {
                        Id = id,
                        Length = length,
                        Type = (MessageType)type,
                    };


                    buffer = buffer.Slice(21);
                }
                catch (EndOfStreamException) //Not enough data
                {
                    return false;
                }
            }


            if (buffer.Length >= message.Header.Length)
            {
                //lock (_debug)
                //{
                //    List<string>? list;
                //    if (!_debug.TryGetValue(message.Header.Id.ToString(), out list))
                //    {
                //        list = new List<string>();
                //        _debug[message.Header.Id.ToString()] = list;
                //    }

                //    list.Add($"Received header : {message.Header.Type.ToString()}");
                //}

                message.Body = buffer.Slice(0, message.Header.Length);
                buffer = buffer.Slice(message.Header.Length);

                return true;
            }
            else
            {


                return false;
            }

        }

        internal async Task ProcessRequest(TcpPeerRequestContext rq)
        {

            Debug.Assert(rq.RemotePeer != null);
            Debug.Assert(rq.ResponsePipe != null);
            Debug.Assert(rq.RequestPipe != null);

            try
            {


                _ = ReadSendPipe(rq);
                var handler = rq.OperationHandler;
                if (handler != null)
                {

                    try
                    {
                        if (handler.Operation != BROADCAST_OPERATION)
                        {
                            await onRequestStarted(rq);
                        }


                        rq.Stack = handler.Operation;
                        await handler.Handler.Invoke(rq);
                        await rq.Writer.FlushAsync();
                    }
                    catch (RequestException)
                    {
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger?.Invoke($"an error occured while processing a request '{rq.Operation}' (3118).", ex);
                        throw new RequestException(TcpRequestErrorCodes.OperationError, ex);
                    }
                    finally
                    {
                        if (TryRemoveFromPendingContexts(rq.Id, out _, "2518"))
                        {
                            rq.SetInputWriteCompleted();
                            ////Tests: Console.Writeline("Remove:"+rq.Id);
                        }
                    }
                    rq.SetCompleted();
                }
                else
                {
                    if (TryRemoveFromPendingContexts(rq.Id, out _, "2527"))
                    {
                        ////Tests: Console.Writeline("Remove:"+rq.Id);
                    }
                    rq.SetCompleted(new RequestException(TcpRequestErrorCodes.OperationNotFound));
                }
            }
            catch (RequestException ex)
            {
                rq.SetCompleted(ex);
            }

        }



        /// <summary>
        /// Pending requests on the server.
        /// </summary>
        private Dictionary<Guid, TcpPeerRequestContext> _pendingRequestContexts = new Dictionary<Guid, TcpPeerRequestContext>();
        private object _pendingRequestContextsSyncRoot = new object();


        private bool TryAddToPendingContexts(TcpPeerRequestContext ctx)
        {
            lock (_pendingRequestContextsSyncRoot)
            {
                if (_pendingRequestContexts.TryAdd(ctx.Id, ctx))
                {
                    //Debug.WriteLine($"Successfully added Ctx {ctx.Id}");
                    return true;
                }
                else
                {
                    //Debug.WriteLine($"Failed to add ctx {ctx.Id}");
                    return false;
                }
            }
        }

        private bool TryRemoveFromPendingContexts(Guid id, [NotNullWhen(true)] out TcpPeerRequestContext? ctx, string debugId)
        {
            lock (_pendingRequestContextsSyncRoot)
            {
                if (_pendingRequestContexts.Remove(id, out ctx))
                {
                    //Debug.WriteLine($"Successfully removed ctx {id} ({debugId})");
                    return true;
                }
                else
                {
                    //Debug.WriteLine($"Failed to remove ctx {id}  ({debugId})");
                    return false;
                }
            }
        }

        private bool TryGetPendingContext(Guid id, [NotNullWhen(true)] out TcpPeerRequestContext? ctx)
        {
            lock (_pendingRequestContextsSyncRoot)
            {
                return _pendingRequestContexts.TryGetValue(id, out ctx);
            }
        }

        /// <summary>
        /// Pending responses on the client.
        /// </summary>
        private Dictionary<Guid, TcpRequest> pendingRequests = new Dictionary<Guid, TcpRequest>();

        private object _pendingResponsesSyncRoot = new object();

        private bool TryAddToPendingRequests(TcpRequest rq)
        {
            lock (_pendingResponsesSyncRoot)
            {
                if (pendingRequests.TryAdd(rq.Id, rq))
                {
                    //Debug.WriteLine($"Successfully added request {rq.Id}");
                    return true;
                }
                else
                {
                    //Debug.WriteLine($"Failed to add request {rq.Id}");
                    return false;
                }
            }
        }
        private bool TryRemoveFromPendingRequests(Guid id, [NotNullWhen(true)] out TcpRequest? request, string debugId)
        {
            lock (_pendingResponsesSyncRoot)
            {
                if (pendingRequests.Remove(id, out request))
                {
                    //Debug.WriteLine($"Successfully removed request {id} ({debugId})");
                    return true;
                }
                else
                {
                    //Debug.WriteLine($"Failed to remove request {id}  ({debugId})");
                    return false;
                }
            }
        }

        private bool TryGetPendingRequest(Guid id, [NotNullWhen(true)] out TcpRequest? request)
        {
            lock (_pendingResponsesSyncRoot)
            {
                return pendingRequests.TryGetValue(id, out request);
            }
        }
        private object _lock = new object();
        private int _last;
        private DateTime _lastComputedOn;
        private int _current;
        private ReadOnlySequence<byte> GetOperationName(ReadOnlySequence<byte> body, out int nextOffset)
        {
            var len = body.FirstSpan[0];
            nextOffset = len + 1;
            return body.Slice(0, len + 1);
        }
        private void ProcessMessageAsync(Message message, IPEndPoint receivedOn, RemotePeer connection)
        {

            var type = message.Header.Type;
            if (connection.Connection == null)
            {
                return;
            }

            //lock (_debug)
            //{
            //    List<string>? list;
            //    if (!_debug.TryGetValue(message.Header.Id.ToString(), out list))
            //    {
            //        list = new List<string>();
            //        _debug[message.Header.Id.ToString()] = list;
            //    }

            //    list.Add($"Received : {message.Header.Type.ToString()}");
            //}


            if (type.HasFlag(MessageType.Request)) //Requests
            {
                var body = message.Body;

                if (type.HasFlag(MessageType.Begin))
                {
                    _received.Add(1);
                    var opName = GetOperationName(body, out var consumed);
                    body = body.Slice(consumed);

                    _handlers.TryGet(opName, out var handler);

                    var rq = _requestPool.Get();

                    rq.Init(message.Header.Id, _requestPool, handler, connection, connection.Endpoint, receivedOn);
                    if (TryAddToPendingContexts(rq))
                    {


                        ////Tests: Console.Writeline("processing:" + message.Header.Id);

                        var startTicks = _watch.ElapsedTicks;
                        try
                        {
                            lock (_lock)
                            {
                                _current++;
                                if (_lastComputedOn + TimeSpan.FromSeconds(10) < DateTime.UtcNow)
                                {
                                    _lastComputedOn = DateTime.UtcNow;
                                    _last = _current;
                                    _current = 0;
                                    ThreadPool.GetMaxThreads(out var workerThreads, out var completionPortThreads);

                                    //logger.Log(LogLevel.Info, "tcppeer", $"scheduled {_last} operations. threads={ThreadPool.ThreadCount} pendingTasks={ThreadPool.PendingWorkItemCount} maxThreads ={workerThreads} maxIOCompletion={completionPortThreads}", new { });
                                }
                            }

                            ThreadPool.UnsafeQueueUserWorkItem(rq, preferLocal: false);
                        }
                        catch
                        {
                            EnqueueError(connection.Connection.Output, message.Header.Id, 504);
                        }

                        var elapsedTicks = _watch.ElapsedTicks - startTicks;
                        _processingTime.Record(((double)elapsedTicks) / Stopwatch.Frequency * 1000);
                    }
                }
                if (type.HasFlag(MessageType.Content))
                {
                    if (TryGetPendingContext(message.Header.Id, out var ctx))
                    {

                        ctx.ProcessRequestContent(ref body);


                    }
                }

                if (type.HasFlag(MessageType.End))
                {
                    if (TryRemoveFromPendingContexts(message.Header.Id, out var request, "2687"))
                    {
                        ////Tests: Console.Writeline("Remove:"+message.Header.Id);
                        request.SetInputWriteCompleted();

                    }
                }

            }
            else if (type.HasFlag(MessageType.Response))
            {
                if (type.HasFlag(MessageType.Content))
                {
                    if (TryGetPendingRequest(message.Header.Id, out var response))
                    {

                        response.ProcessResponseContent(message);


                    }
                }
                if (type.HasFlag(MessageType.End))
                {
                    if (TryRemoveFromPendingRequests(message.Header.Id, out var response, "2681"))
                    {
                        //Debug.WriteLine($"{stopwatch.Elapsed}, Removed tcpPeer 2631 {message.Header.Id}");
                        Debug.Assert(response.receivePipe != null);

                        response.SetOutputCompleted();
                    }
                }
            }
            //Debug.WriteLine($"{stopwatch.Elapsed} Received {message.Header.Id} {Enum.GetName(message.Header.Type)}");

            //Interlocked.Add(ref _acc.TotalBytesReceived, message.Header.Length + 21);
            //logger.Debug($"{Id}: Received {message.Header.Type}, id={message.Header.Id}, length={message.Body.Length}");
            switch (type)
            {


                case MessageType.Error:
                    {
                        if (TryRemoveFromPendingRequests(message.Header.Id, out var response, "2692"))
                        {
                            static void ProcessError(Message message, TcpRequest rq)
                            {
                                Debug.Assert(rq.receivePipe != null);
                                int code;
                                var seqReader = new SequenceReader<byte>(message.Body);
                                seqReader.TryReadLittleEndian(out code);
                                rq.SetInputCompleted(new RequestException(code));
                                rq.SetOutputCompleted(new RequestException(code));
                            }

                            ProcessError(message, response);
                            //Debug.WriteLine($"{stopwatch.Elapsed}, Removed tcpPeer 2648 {message.Header.Id}");

                        }
                    }
                    break;

                case MessageType.Canceled:
                    {
                        if (TryRemoveFromPendingContexts(message.Header.Id, out var request, "2748"))
                        {
                            request.Cancel();
                            ////Tests: Console.Writeline("Remove:"+message.Header.Id);
                            request.SetInputWriteCompleted();


                        }
                    }
                    break;
                default:

                    break;
            }

        }

        /// <summary>
        /// Disposes the object.
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);

            foreach (var peer in _remoteSockets.Values)
            {
                peer.Metadata.Dispose();
                peer.Connection.Socket.Close();
            }
            ShutdownServer();
        }
    }

    /// <summary>
    /// Represents a request as seen by a <see cref="TcpPeer"/> server.
    /// </summary>
    public class TcpPeerRequestContext : IThreadPoolWorkItem
    {
        internal readonly Pipe RequestPipe = new Pipe();
        internal readonly Pipe ResponsePipe = new Pipe();
        private readonly TcpPeer peer;

        internal TcpPeerRequestContext(TcpPeer peer)
        {
            this.peer = peer;
        }

        CancellationTokenSource? cts = null;
        bool isLinkedcts = false;
        internal void Init(Guid id, DefaultObjectPool<TcpPeerRequestContext> ctxPool, OperationHandler? operation, RemotePeer? connection, IPEndPoint origin, IPEndPoint receivedOn, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.CanBeCanceled)
            {
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                isLinkedcts = true;
            }
            else if (cts == null || cts.IsCancellationRequested)
            {
                cts = new CancellationTokenSource();
            }
            //Console.WriteLine($"Init {Id}=>{id} : {operation.Operation}");
            _isOutputReadComplete = false;
            _isRequestHandlingComplete = false;
            _isInputWriteComplete = false;


            if (ctxPool is null)
            {
                throw new ArgumentNullException(nameof(ctxPool));
            }

            this.ctxPool = ctxPool;
            Id = id;
            RemotePeer = connection;
            _remoteEndpoint = origin;
            _localEndpoint = receivedOn;

            OperationHandler = operation;
            _operation = operation?.Operation ?? string.Empty;


            lastFlush = default;
            State = null;

            if (RemotePeer != null)
            {
                lock (RemotePeer.RequestsReceivedFrom)
                {

                    RemotePeer.RequestsReceivedFrom.Add(new PendingOperationInfo(Id, this));
                }
            }
        }
        public string? Stack { get; set; }

        private DefaultObjectPool<TcpPeerRequestContext>? ctxPool;

        public void Execute()
        {
            _ = peer.ProcessRequest(this);
        }


        /// <summary>
        /// Gets the id of the request.
        /// </summary>
        public Guid Id { get; private set; }

        /// <summary>
        /// Reads the request data.
        /// </summary>
        public PipeReader Reader
        {
            get
            {
                Debug.Assert(RequestPipe != null);
                return RequestPipe.Reader;
            }
        }

        /// <summary>
        /// Writes the response data;
        /// </summary>
        public PipeWriter Writer
        {
            get
            {
                Debug.Assert(ResponsePipe != null);
                return ResponsePipe.Writer;

            }
        }

        private string? _operation;
        /// <summary>
        /// Gets the operation.
        /// </summary>
        public string Operation
        {
            get
            {
                Debug.Assert(_operation != null);

                return _operation;
            }
        }

        private IPEndPoint? _remoteEndpoint;
        /// <summary>
        /// Endpoint of the remote peer.
        /// </summary>
        public IPEndPoint RemoteEndpoint
        {
            get
            {
                Debug.Assert(_remoteEndpoint != null);
                return _remoteEndpoint;
            }
        }

        private IPEndPoint? _localEndpoint;

        /// <summary>
        /// Gets the endpoint that received the request.
        /// </summary>
        public IPEndPoint LocalEndpoint
        {
            get
            {
                Debug.Assert(_localEndpoint != null);
                return _localEndpoint;

            }
        }

        internal object SyncRoot = new object();
        internal PipeWriter RequestWriter
        {
            get
            {
                Debug.Assert(RequestPipe != null);

                return RequestPipe.Writer;

            }
        }

        internal ValueTask<FlushResult> lastFlush;

        /// <summary>
        /// Represents the remote peer.
        /// </summary>
        public RemotePeer? RemotePeer { get; private set; }


        /// <summary>
        /// Custom state associated with the request.
        /// </summary>
        public object? State { get; set; }

        /// <summary>
        /// Gets the token cancelled if the request is cancelled.
        /// </summary>
        /// <remarks>
        /// A request can be cancelled by a client or if the client disconnects.
        /// TODO: To be implemented.
        /// </remarks>
        public CancellationToken CancellationToken => cts.Token;

        internal OperationHandler? OperationHandler { get; private set; }


        internal void Reset()
        {
            //Console.WriteLine($"Reset {Id}");
            Debug.Assert(RequestPipe != null && ResponsePipe != null);

            RequestPipe.Reset();
            ResponsePipe.Reset();


            if (RemotePeer != null)
            {
                lock (RemotePeer.RequestsReceivedFrom)
                {
                    RemotePeer.RequestsReceivedFrom.Remove(new PendingOperationInfo(Id, null));
                }
                RemotePeer = null;
            }
            _operation = null;
            _remoteEndpoint = null;
            _localEndpoint = null;
            if (cts != null && (isLinkedcts || cts.IsCancellationRequested))
            {
                cts.Dispose();
                isLinkedcts = false;
                cts = null;
            }

        }

        bool _isOutputReadComplete = false;
        bool _isInputWriteComplete = false;
        bool _isRequestHandlingComplete = false;

        internal void SetOutputReadCompleted(Exception? ex = null)
        {

            lock (SyncRoot)
            {
                if (!_isOutputReadComplete)
                {
                    //Console.WriteLine($"SetOutputReadCompleted {Id}");
                    _isOutputReadComplete = true;
                    Debug.Assert(ResponsePipe != null);

                    ResponsePipe.Reader.Complete(ex);

                    Return();
                }
            }
        }
        internal void SetInputWriteCompleted(Exception? ex = null)
        {
            lock (SyncRoot)
            {
                if (!_isInputWriteComplete)
                {
                    _isInputWriteComplete = true;
                    Debug.Assert(RequestPipe != null);
                    RequestPipe.Writer.Complete(ex);
                    Return();
                }
            }
        }

        void Return()
        {
            if (_isOutputReadComplete && _isRequestHandlingComplete && _isInputWriteComplete)
            {
                Debug.Assert(ctxPool != null);

                ctxPool.Return(this);
            }
        }
        internal void SetCompleted(RequestException? ex = null)
        {
            lock (SyncRoot)
            {
                if (!_isRequestHandlingComplete)
                {
                    _isRequestHandlingComplete = true;
                    Debug.Assert(RequestPipe != null && ResponsePipe != null);

                    RequestPipe.Reader.Complete(ex);
                    ResponsePipe.Writer.Complete(ex);

                    Return();
                }
            }
        }

        internal void Cancel()
        {
            if (cts != null)
            {
                cts.Cancel();
            }
        }

        private static long WriteToOutput(PipeWriter output, ref ReadOnlySequence<byte> message)
        {
            int remaining = (int)message.Length;
            int written = 0;

            while (remaining > 0)
            {
                var span = output.GetMemory(remaining).Span;
                var length = remaining < span.Length ? remaining : span.Length;
                message.Slice(written, length).CopyTo(span);
                output.Advance(length);
                written += length;
                remaining -= length;
            }
            return message.Length;

        }

        internal ValueTask<FlushResult> ProcessRequestContent(ref ReadOnlySequence<byte> message)
        {


            lock (SyncRoot)
            {
                if (!_isInputWriteComplete)
                {
                    WriteToOutput(RequestWriter, ref message);

                }

            }

            return RequestWriter.FlushAsync();




        }
    }
}
