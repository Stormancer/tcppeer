# TCPPeer
Stormancer Node 2 Node TCP Transport

## Features
- Interlaced requests and processing
- Constent string remote operation ids
- System.IO.Pipelines based request and response streaming
- Request broadcasting across the peers mesh, responses exposed as an `IAsyncEnumerable<Response>` (request & response streaming supported too, however new peers added to the mesh after the start of a broadcast request won't process it)

## TODO
- Refactor into more classes/files, especially separate the TcpPeer client class from the internal transport class
- Add SSL support
- Refactor the pipe completion code. It's currently a mess.
- Provides a way to manually start a request after creation to eliminate scheduling when the request body can be fully written before sending.
