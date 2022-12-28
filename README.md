# TCPPeer
Stormancer Node 2 Node TCP Transport

## Features
- Interlaced requests and processing
- Constent string remote operation ids
- System.IO.Pipelines based request and response streaming
- Request broadcasting across the peers mesh, responses exposed as an `IAsyncEnumerable<Response>` (request & response streaming supported too, however new peers added to the mesh after the start of a broadcast request won't process it)

## Nuget

https://www.nuget.org/packages/Stormancer.Tcp

## Usage

### Declare operation handlers

To declare operation handlers, create a class inheriting from `Stormancer.Tcp.ITcpPeerConfigurator` and implement the `Configure` method: 


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


### Start a peer as server

    var server = new TcpPeer("test-server", Log, () => new ITcpPeerConfigurator[] { configurator }, _ => { }, _ => Task.CompletedTask, _ => { }, (_, _) => { });

    //For the sample, we start a server on a random port by providing 0 as the port.
    _ = server.RunAsync(new System.Net.IPEndPoint(IPAddress.Any, 0), CancellationToken.None);

### Connect a client.

    var client = new TcpPeer("test- client", Log, () => Enumerable.Empty<ITcpPeerConfigurator>(), _ => { }, _ => Task.CompletedTask, _ => { }, (_, _) => { });

    var endpoint = new IPEndPoint(IPAddress.Loopback, server.LocalEndpoints.First().Port);

    await client.Connect(endpoint);

TcpPeer instance can be both clients and servers at the same time. It's possible to create complex topologies of peers by starting them all as servers, then connecting to remote peers :

     _ = peer.RunAsync(new System.Net.IPEndPoint(IPAddress.Any, 0), CancellationToken.None);
     await peer.Connect(endpoint1);
     await client.Connect(endpoint2);

### Sending a request

    ReadOnlyMemory<byte> seq = TestSequence();
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

### Sending a broadcast request

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

## TODO
1. Refactor into more classes/files, especially separate the TcpPeer client class from the internal transport class.
2. Create a Configuration object to enable creating a peer without providing a bunch of lambdas.
3. Add support for SSL!
4. Refactor the pipe completion code. It's currently a mess. Probably necessary to do 1.
5. Provides a way to manually start a request after creating the request object to eliminate scheduling when the request body can be fully written before sending.
