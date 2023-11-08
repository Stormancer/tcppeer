using MessagePack.Resolvers;
using Microsoft.Extensions.Options;
using Stormancer.Tcp.Tests;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Stormancer.Networking.Reliable.Tests
{

    public class NetworkCommunicationTests
    {

        [Fact(DisplayName = "Test bind")]
        public async Task TestBind()
        {
            var options = new PeerOptions();
            options.TransportsOptions.Endpoints.Add(new TransportConfiguration { BindingEndpoint = new IPEndPoint(IPAddress.Loopback, 8091) });
            await using var client = new MeshClient(options);

            await client.StartAsync(CancellationToken.None);

        }

        [Fact(DisplayName = "Test connect")]
        public async Task TestConnect()
        {
            var serverEndpoint = new IPEndPoint(IPAddress.Loopback, 8091);
            var notifier1 = new ConnectionSuccessNotifier();

            var options1 = new PeerOptions();
            options1.TransportsOptions.UseConnectionSuccessMiddleware(notifier1);

            options1.TransportsOptions.Endpoints.Add(new TransportConfiguration { BindingEndpoint = serverEndpoint });
            await using var client1 = new MeshClient(options1);

            var options2 = new PeerOptions();
            var notifier2 = new ConnectionSuccessNotifier();
            options2.TransportsOptions.UseConnectionSuccessMiddleware(notifier2);
            options2.TransportsOptions.Endpoints.Add(new TransportConfiguration { BindingEndpoint = new IPEndPoint(IPAddress.Loopback, 8092) });
            await using var client2 = new MeshClient(options2);

            await client1.StartAsync(CancellationToken.None);
            await client2.StartAsync(CancellationToken.None);

            await client2.ConnectAsync(serverEndpoint, CancellationToken.None);


            await notifier1.WaitAsync(TimeSpan.FromSeconds(1));
            await notifier2.WaitAsync(TimeSpan.FromSeconds(1));
        }

        [Fact(DisplayName = "Test basic routes")]
        public async Task TestBasicRoutesAdded()
        {
            var serverEndpoint = new IPEndPoint(IPAddress.Loopback, 8091);

            var options1 = new PeerOptions();
            options1.TransportsOptions.Endpoints.Add(new TransportConfiguration { BindingEndpoint = serverEndpoint });
            var client1 = new MeshClient(options1);

            Assert.True(client1.HasRoute(client1.Id, out var hops) && hops == 0);

            var options2 = new PeerOptions();
            options2.TransportsOptions.Endpoints.Add(new TransportConfiguration { BindingEndpoint = new IPEndPoint(IPAddress.Loopback, 8092) });
            var client2 = new MeshClient(options2);

            await client1.StartAsync(CancellationToken.None);
            await client2.StartAsync(CancellationToken.None);

            await client2.ConnectAsync(serverEndpoint, CancellationToken.None);


            {
                Assert.True(client2.HasRoute(client1.Id, out hops) && hops == 1);
                Assert.True(client1.HasRoute(client2.Id, out hops) && hops == 1);
            }


            await client1.DisposeAsync();
            await client2.DisposeAsync();
        }


        [Fact(DisplayName = "Test get connected peers")]
        public async Task TestConnectedPeers()
        {

            var serverEndpoint = new IPEndPoint(IPAddress.Loopback, 8091);

            var options1 = new PeerOptions();
            options1.TransportsOptions.Endpoints.Add(new TransportConfiguration { BindingEndpoint = serverEndpoint });
            var client1 = new MeshClient(options1);



            var options2 = new PeerOptions();
            options2.TransportsOptions.Endpoints.Add(new TransportConfiguration { BindingEndpoint = new IPEndPoint(IPAddress.Loopback, 8092) });
            var client2 = new MeshClient(options2);

            await client1.StartAsync(CancellationToken.None);
            await client2.StartAsync(CancellationToken.None);

            Assert.Empty(client2.ConnectedPeers);
            Assert.Empty(client1.ConnectedPeers);


            await client2.ConnectAsync(serverEndpoint, CancellationToken.None);


            {
                Assert.Single(client1.ConnectedPeers);
                Assert.Single(client2.ConnectedPeers);
            }


            await client1.DisposeAsync();
            await client2.DisposeAsync();
        }

    }

}
