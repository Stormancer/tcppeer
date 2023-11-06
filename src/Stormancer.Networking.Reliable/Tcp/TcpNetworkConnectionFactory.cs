


using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;

namespace Stormancer.Networking.Reliable.Tcp
{

    /// <summary>
    /// Creates Network connections.
    /// </summary>
    /// <remarks>
    /// Inspired by the implementation of SocketConnectionContextFactory.cs in Kestrel
    /// </remarks>
    internal class TcpNetworkConnectionFactory : INetworkConnectionFactory, IDisposable
    {
        private sealed class QueueSettings
        {
            public PipeScheduler Scheduler { get; init; } = default!;
            public PipeOptions InputOptions { get; init; } = default!;
            public PipeOptions OutputOptions { get; init; } = default!;
            public SocketSenderPool SocketSenderPool { get; init; } = default!;
            public MemoryPool<byte> MemoryPool { get; init; } = default!;
        }

        private readonly ILogger<TcpNetworkConnectionFactory> _logger;
        private readonly int _settingsCount;
        private readonly QueueSettings[] _settings;
        private readonly ILoggerFactory _loggerFactory;
        private int _settingsIndex;

        public TcpNetworkConnectionFactory(SocketTransportOptions options, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _logger = loggerFactory.CreateLogger<TcpNetworkConnectionFactory>();

            _settingsCount = options.IOQueueCount;

            var maxReadBufferSize = options.MaxReadBufferSize ?? 0;
            var maxWriteBufferSize = options.MaxWriteBufferSize ?? 0;
            var applicationScheduler = PipeScheduler.ThreadPool;

            if (_settingsCount > 0)
            {
                _settings = new QueueSettings[_settingsCount];

                for (var i = 0; i < _settingsCount; i++)
                {
                    var memoryPool = options.MemoryPoolFactory();
                    var transportScheduler = new IOQueue();

                    _settings[i] = new QueueSettings()
                    {
                        Scheduler = transportScheduler,
                        InputOptions = new PipeOptions(memoryPool, applicationScheduler, transportScheduler, maxReadBufferSize, maxReadBufferSize / 2, useSynchronizationContext: false),
                        OutputOptions = new PipeOptions(memoryPool, transportScheduler, applicationScheduler, maxWriteBufferSize, maxWriteBufferSize / 2, useSynchronizationContext: false),
                        SocketSenderPool = new SocketSenderPool(PipeScheduler.Inline),
                        MemoryPool = memoryPool,
                    };
                }
            }
            else
            {
                var memoryPool = options.MemoryPoolFactory();
                var transportScheduler = PipeScheduler.ThreadPool;

                _settings = new QueueSettings[]
                {
                new QueueSettings()
                {
                    Scheduler = transportScheduler,
                    InputOptions = new PipeOptions(memoryPool, applicationScheduler, transportScheduler, maxReadBufferSize, maxReadBufferSize / 2, useSynchronizationContext: false),
                    OutputOptions = new PipeOptions(memoryPool, transportScheduler, applicationScheduler, maxWriteBufferSize, maxWriteBufferSize / 2, useSynchronizationContext: false),
                    SocketSenderPool = new SocketSenderPool(PipeScheduler.Inline),
                    MemoryPool = memoryPool,
                }
                };
                _settingsCount = 1;
            }
            _loggerFactory = loggerFactory;
        }

        public NetworkConnection Create(Socket socket, EndPointConfig? endPointConfig, bool isClient)
        {
            var setting = _settings[Interlocked.Increment(ref _settingsIndex) % _settingsCount];

            var connection = new TcpSocketNetworkConnection(socket,
                setting.MemoryPool,
                setting.SocketSenderPool.Scheduler,
                _loggerFactory,
                setting.SocketSenderPool,
                setting.InputOptions,
                setting.OutputOptions,
                endPointConfig?.Features,
                isClient
               );


            connection.Start();
            return connection;
        }

        public void Dispose()
        {
            // Dispose any pooled senders and memory pools
            foreach (var setting in _settings)
            {
                setting.SocketSenderPool.Dispose();
                setting.MemoryPool.Dispose();
            }
        }
    }

}
