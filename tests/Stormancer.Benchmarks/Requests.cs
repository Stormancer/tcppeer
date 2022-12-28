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
    [SimpleJob(runtimeMoniker: RuntimeMoniker.Net70, warmupCount: 5, targetCount: 20)]
    [SimpleJob(runtimeMoniker: RuntimeMoniker.Net60, warmupCount: 5, targetCount: 20)]
    public class Requests : ITcpPeerConfigurator
    {

        private const int SEGMENTS = 1;
        private byte[] TestSequence() => Enumerable.Range(0, SEGMENTS * 64).Select(i => (byte)i).ToArray();
        private readonly ReadOnlyMemory<byte> seq;

        public Requests()
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
        private const int NB_PEERS = 2;
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
        public Task Requests_100k()
        {



            async Task RunRequest(IPEndPoint server, TcpPeer client)
            {

                using (var request = client.Send(server, "test", default))
                {

                    for (int i = 0; i < SEGMENTS; i++)
                    {
                        await request.Writer.WriteAsync(seq.Slice(64 * i, 64));
                    }

                    request.Writer.Complete();
                    var readResult = await request.Reader.ReadAtLeastAsync(SEGMENTS * 64);
                    ReadOnlyMemory<byte> array = readResult.Buffer.Slice(0, SEGMENTS * 64).ToArray();
                    Debug.Assert(seq.Span.SequenceEqual(array.Span));
                    request.Reader.Complete();


                }


            }
            var tasks = new List<Task>();

            var client = servers[0];
            var server = client.ConnectedPeers.First().Endpoint;
            for (int i = 0; i < 100000; i++)
            {
                tasks.Add(RunRequest(server, client));
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
