using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Stormancer.Tcp.Tests
{
  
    public class NetworkCommunicationTests : ITcpPeerConfigurator
    {
        private const int KnownErrorCode = 1023;

        private const int SEGMENTS = 3;
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

            builder.ConfigureHandler("connectionDrop", async ctx =>
            {
                //Wait for data.
                //No data sent.
                var readResult = await ctx.Reader.ReadAsync();


            });
            builder.ConfigureHandler("unknownError", ctx =>
            {
                throw new InvalidOperationException();
            });

            builder.ConfigureHandler("knownError", ctx =>
            {
                throw new RequestException(KnownErrorCode);
            });
        }
        private void Log(string msg, Exception ex) { }
        private byte[] TestSequence() => Enumerable.Range(0, SEGMENTS * 64).Select(i => (byte)i).ToArray();
        [Fact]
        public async Task RequestToServerTest()
        {
            var server = new TcpPeer("server", Log, () => new ITcpPeerConfigurator[] { this }, _ => { }, _ => Task.CompletedTask, _ => { }, (_, _) => { });

            _ = server.RunAsync(new System.Net.IPEndPoint(IPAddress.Any, 0), CancellationToken.None);

            var client = new TcpPeer("client", Log, () => Enumerable.Empty<ITcpPeerConfigurator>(), _ => { }, _ => Task.CompletedTask, _ => { }, (_, _) => { });

            Debug.Assert(server.LocalEndpoints != null);

            Debug.Assert(server.LocalEndpoints.Any());

            var endpoint = new IPEndPoint(IPAddress.Loopback, server.LocalEndpoints.First().Port);

            await client.Connect(endpoint);

            ReadOnlyMemory<byte> seq = TestSequence();


            async Task RunRequest()
            {
                using (var request = client.Send(endpoint, "test", default))
                {

                    for (int i = 0; i < SEGMENTS; i++)
                    {
                        await request.Writer.WriteAsync(seq.Slice(64 * i, 64));
                    }


                    request.Writer.Complete();

                    var readResult = await request.Reader.ReadAtLeastAsync(SEGMENTS * 64);

                    ReadOnlyMemory<byte> array = readResult.Buffer.Slice(0, SEGMENTS * 64).ToArray();
                    Debug.Assert(seq.Span.SequenceEqual(array.Span));
                }
            }
            var tasks = new List<Task>();

            for (int i = 0; i < 1; i++)
            {
                tasks.Add(RunRequest());
            }

            await Task.WhenAll(tasks);

            server.ShutdownServer();


        }


        [Fact]
        public async Task Broadcast()
        {
         
            var server = new TcpPeer("server", Log, () => new ITcpPeerConfigurator[] { this }, _ => { }, _ => Task.CompletedTask, _ => { }, (_, _) => { });

            _ = server.RunAsync(new System.Net.IPEndPoint(IPAddress.Any, 0), CancellationToken.None);

            var client = new TcpPeer("client", Log, () => Enumerable.Empty<ITcpPeerConfigurator>(), _ => { }, _ => Task.CompletedTask, _ => { }, (_, _) => { });
            _ = client.RunAsync(new System.Net.IPEndPoint(IPAddress.Any, 0), CancellationToken.None);
            Debug.Assert(server.LocalEndpoints != null);

            Debug.Assert(server.LocalEndpoints.Any());

            var endpoint = new IPEndPoint(IPAddress.Loopback, server.LocalEndpoints.First().Port);

            await client.Connect(endpoint);

            ReadOnlyMemory<byte> seq = TestSequence();

            var nb = 0;
            async Task RunRequest()
            {
                using (var request = client.Broadcast("test", default))
                {

                    for (int i = 0; i < SEGMENTS; i++)
                    {
                        await request.Writer.WriteAsync(seq.Slice(64 * i, 64));
                    }


                    request.Writer.Complete();

                    await foreach (var response in request.GetResponses())
                    {
                        nb++;
                        var readResult = await response.Reader.ReadAtLeastAsync(SEGMENTS * 64);
                        ReadOnlyMemory<byte> array = readResult.Buffer.Slice(0, SEGMENTS * 64).ToArray();
                        Debug.Assert(seq.Span.SequenceEqual(array.Span));
                        response.Reader.Complete();
                    }
                }
            }
            var tasks = new List<Task>();

            for (int i = 0; i < 1; i++)
            {
                tasks.Add(RunRequest());
            }

            await Task.WhenAll(tasks);

            server.ShutdownServer();
            Debug.Assert(nb == 2);

        }

        [Fact]
        public async Task BroadcastChain()
        {
            var nbPeers = 20;
           

            IPEndPoint? endpoint = null;
            TcpPeer[] servers = new TcpPeer[nbPeers];
            
            for (int i = 0; i < nbPeers; i++)
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

            

            ReadOnlyMemory<byte> seq = TestSequence();


            async Task RunRequest(TcpPeer server)
            {
                var watch = new Stopwatch();
                watch.Start();
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

                    Debug.Assert(nbResponses == nbPeers);
                }
                Debug.WriteLine(watch.ElapsedMilliseconds);
                
            }
            var tasks = new List<Task>();

            for (int i = 0; i < 1; i++)
            {
                await (RunRequest(servers[0]));
            }

            await Task.WhenAll(tasks);

            foreach (var server in servers)
            {
                server.ShutdownServer();
            }


        }


        [Fact]
        public async Task RequestToClientTest()
        {
            IPEndPoint? clientEndpoint = null;
          
            var server = new TcpPeer("server", Log, () => new ITcpPeerConfigurator[] { this }, _ => { }, _ => Task.CompletedTask, remotePeer => { clientEndpoint = remotePeer.Endpoint; }, (_, _) => { });

            _ = server.RunAsync(new System.Net.IPEndPoint(IPAddress.Any, 0), CancellationToken.None);

            var client = new TcpPeer("client", Log, () => new ITcpPeerConfigurator[] { this }, _ => { }, _ => Task.CompletedTask, _ => { }, (_, _) => { });

            Debug.Assert(server.LocalEndpoints != null);

            Debug.Assert(server.LocalEndpoints.Any());

            var endpoint = new IPEndPoint(IPAddress.Loopback, server.LocalEndpoints.First().Port);
            await client.Connect(endpoint);
            //Connecting must have updated clientEndpoint.
            Debug.Assert(clientEndpoint != null);


            ReadOnlyMemory<byte> seq = TestSequence();


            using (var request = server.Send(clientEndpoint, "test", default))
            {
                for (int i = 0; i < SEGMENTS; i++)
                {
                    await request.Writer.WriteAsync(seq.Slice(64 * i, 64));
                }


                request.Writer.Complete();

                var readResult = await request.Reader.ReadAtLeastAsync(SEGMENTS * 64);

                ReadOnlyMemory<byte> array = readResult.Buffer.Slice(0, SEGMENTS * 64).ToArray();
                Debug.Assert(seq.Span.SequenceEqual(array.Span));
            }

            server.ShutdownServer();

        }


        [Fact]
        public async Task ConnectionLostTestClient()
        {
            
            var server = new TcpPeer("server", Log, () => new ITcpPeerConfigurator[] { this }, _ => { }, _ => Task.CompletedTask, _ => { }, (_, _) => { });

            _ = server.RunAsync(new System.Net.IPEndPoint(IPAddress.Any, 0), CancellationToken.None);

            var client = new TcpPeer("client", Log, () => Enumerable.Empty<ITcpPeerConfigurator>(), _ => { }, _ => Task.CompletedTask, _ => { }, (_, _) => { });

            Debug.Assert(server.LocalEndpoints != null);

            Debug.Assert(server.LocalEndpoints.Any());

            var endpoint = new IPEndPoint(IPAddress.Loopback, server.LocalEndpoints.First().Port);
            await client.Connect(endpoint);


            static async Task SendRequest(TcpPeer client, IPEndPoint serverEndpoint)
            {

                using (var request = client.Send(serverEndpoint, "connectionDrop", default))
                {

                    await request.Reader.ReadAsync();
                }


            }

            var t = SendRequest(client, endpoint);
            server.ShutdownServer();

            try
            {
                await t;
            }
            catch (RequestException ex) when (ex.Code == TcpRequestErrorCodes.ConnectionLost)
            {

            }
        }


        [Fact]
        public async Task OperationNotFoundTest()
        {
          
            var server = new TcpPeer("server", Log, () => new ITcpPeerConfigurator[] { this }, _ => { }, _ => Task.CompletedTask, _ => { }, (_, _) => { });

            _ = server.RunAsync(new System.Net.IPEndPoint(IPAddress.Any, 0), CancellationToken.None);

            var client = new TcpPeer("client", Log, () => Enumerable.Empty<ITcpPeerConfigurator>(), _ => { }, _ => Task.CompletedTask, _ => { }, (_, _) => { });

            Debug.Assert(server.LocalEndpoints != null);

            Debug.Assert(server.LocalEndpoints.Any());

            var endpoint = new IPEndPoint(IPAddress.Loopback, server.LocalEndpoints.First().Port);
            await client.Connect(endpoint);


            static async Task SendRequest(TcpPeer client, IPEndPoint serverEndpoint)
            {

                using (var request = client.Send(serverEndpoint, "notFound", default))
                {

                    await request.Reader.ReadAsync();
                }


            }

            var t = SendRequest(client, endpoint);

            try
            {
                await t;
            }
            catch (RequestException ex) when (ex.Code == TcpRequestErrorCodes.OperationNotFound)
            {

            }

            server.ShutdownServer();
        }


        [Fact]
        public async Task UnknwownOperationErrorTest()
        {
        
            var server = new TcpPeer("server", Log, () => new ITcpPeerConfigurator[] { this }, _ => { }, _ => Task.CompletedTask, _ => { }, (_, _) => { });

            _ = server.RunAsync(new System.Net.IPEndPoint(IPAddress.Any, 0), CancellationToken.None);

            var client = new TcpPeer("client", Log, () => Enumerable.Empty<ITcpPeerConfigurator>(), _ => { }, _ => Task.CompletedTask, _ => { }, (_, _) => { });

            Debug.Assert(server.LocalEndpoints != null);

            Debug.Assert(server.LocalEndpoints.Any());

            var endpoint = new IPEndPoint(IPAddress.Loopback, server.LocalEndpoints.First().Port);
            await client.Connect(endpoint);


            static async Task SendRequest(TcpPeer client, IPEndPoint serverEndpoint)
            {

                using (var request = client.Send(serverEndpoint, "unknownError", default))
                {

                    await request.Reader.ReadAsync();
                }


            }

            var t = SendRequest(client, endpoint);

            try
            {
                await t;
            }
            catch (RequestException ex) when (ex.Code == TcpRequestErrorCodes.OperationError)
            {
               
            }

            server.ShutdownServer();
        }


        [Fact]
        public async Task KnwownOperationErrorTest()
        {
           
            var server = new TcpPeer("server", Log, () => new ITcpPeerConfigurator[] { this }, _ => { }, _ => Task.CompletedTask, _ => { }, (_, _) => { });

            _ = server.RunAsync(new System.Net.IPEndPoint(IPAddress.Any, 0), CancellationToken.None);

            var client = new TcpPeer("client", Log, () => Enumerable.Empty<ITcpPeerConfigurator>(), _ => { }, _ => Task.CompletedTask, _ => { }, (_, _) => { });

            Debug.Assert(server.LocalEndpoints.Any());

            var endpoint = new IPEndPoint(IPAddress.Loopback, server.LocalEndpoints.First().Port);
            await client.Connect(endpoint);


            static async Task SendRequest(TcpPeer client, IPEndPoint serverEndpoint)
            {

                using (var request = client.Send(serverEndpoint, "knownError", default))
                {

                    await request.Reader.ReadAsync();
                }


            }

            var t = SendRequest(client, endpoint);

            try
            {
                await t;
            }
            catch (RequestException ex) when (ex.Code == KnownErrorCode)
            {

            }

            server.ShutdownServer();
        }

    }

}
