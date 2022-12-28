using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using Stormancer.Tcp;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Benchmarks
{
    [SimpleJob(runtimeMoniker:RuntimeMoniker.Net70,  warmupCount: 3, targetCount: 10)]
    public class Broadcast : ITcpPeerConfigurator
    {
        
        private const int SEGMENTS = 1;
        private byte[] TestSequence() => Enumerable.Range(0, SEGMENTS * 64).Select(i => (byte)i).ToArray();
        private readonly ReadOnlyMemory<byte> seq;

        public Broadcast()
        {
            seq = TestSequence();
        }

        void ITcpPeerConfigurator.Configure(ITcpPeerApiBuilder builder)
        {
            ReadOnlyMemory<byte> seq = TestSequence();
            builder.ConfigureHandler("test", async ctx =>
            {
                var readResult = await ctx.Reader.ReadAtLeastAsync(SEGMENTS * 64);

                ReadOnlyMemory<byte> array = readResult.Buffer.Slice(0, SEGMENTS * 64).ToArray();
                Debug.Assert(seq.Span.SequenceEqual(array.Span));
                ctx.Reader.AdvanceTo(readResult.Buffer.End);
                ctx.Reader.Complete();

                for (int i = 0; i < SEGMENTS; i++)
                {
                    await ctx.Writer.WriteAsync(array.Slice(64 * i, 64));
                }

                ctx.Writer.Complete();
            });


        }
        private const int NB_PEERS = 20;
        private TcpPeer[] servers = new TcpPeer[NB_PEERS];

        private void Log(string msg, Exception ex) { }

        [GlobalSetup]
        public async Task GlobalSetup()
        {
           
            IPEndPoint? endpoint = null;
           

            for (int i = 0; i < NB_PEERS; i++)
            {
                var server = new TcpPeer("server", Log, () => new ITcpPeerConfigurator[] { this }, _ => { }, _ => Task.CompletedTask, _ => { }, (_, _) => { });

                _ = server.RunAsync(new System.Net.IPEndPoint(IPAddress.Any, 0), CancellationToken.None);
                if (endpoint != null)
                {
                    await server.Connect(endpoint);
                }
                endpoint = new IPEndPoint(IPAddress.Loopback, server.LocalEndpoints.First().Port);

                servers[i] = server;
            }

        }


        [Benchmark]
        public Task BroadcastChain()
        {
           


            async Task RunRequest(TcpPeer server)
            {
              
                using (var request = server.Broadcast("test", default))
                {

                    for (int i = 0; i < SEGMENTS; i++)
                    {
                        await request.Writer.WriteAsync(seq.Slice(64 * i, 64));
                    }


                    request.Writer.Complete();

                    var nbResponses = 0;
                    await foreach (var response in request.GetResponses())
                    {
                        var readResult = await response.Reader.ReadAtLeastAsync(SEGMENTS * 64);
                        ReadOnlyMemory<byte> array = readResult.Buffer.Slice(0, SEGMENTS * 64).ToArray();
                        Debug.Assert(seq.Span.SequenceEqual(array.Span));
                        response.Reader.Complete();
                        nbResponses++;
                    }

                    Debug.Assert(nbResponses == NB_PEERS);
                }
              

            }
            var tasks = new List<Task>();

            for (int i = 0; i < 100;i++)
            {
                tasks.Add(RunRequest(servers[0]));
            }
            return Task.WhenAll(tasks);
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            foreach (var server in servers)
            {
                server.ShutdownServer();
            }
        }
    }
}
