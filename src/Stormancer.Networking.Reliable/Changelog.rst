=========
Changelog
=========

All notable changes to this project will be documented in this file.

The format is based on `Keep a Changelog <https://keepachangelog.com/en/1.0.0/>`_, except reStructuredText is used instead of Markdown.
Please use only reStructuredText in this file, no Markdown!

This project adheres to semantic versioning.

5.0.2.1
----------
Changed
*******
- Improved error logging
Fixed
*****
- Fixed possible crash in send queue processing function.

5.0.1
----------
Changed
*******
- Changed protocol to reduce message count.
- Make the read input method synchronous if possible.
- Remove allocations

4.0.1
-----
Changed
*******
- Removed Serializer from TcpPeer
- Updated to dotnet 7

3.0.1.8
--------
Changed
*******
- Use ThreadPool methods to schedule work.
- Remove error log when failing to send a message.

Fixed
*****
- Fixed race condition when request content was received while the request was completing.
- Fixed deadlock that could occur if a request disposal occured concurrently with a cancellation.

2.2.0.3
----------
Fixed
*****
- Remove possible race condition on request cancellation.

2.1.5.1
----------
Fixed
*****
- Remove callstack saves when no debugger is attached
- Removes debug logs

2.1.4.4
-------
Fixed
*****
- Fixed memory leaks.

2.1.4.2
-------
Changed
*******
- Don't cache pipes to prevent bad usage from creating corrupted data.
Fixed
*****
- Fixed race condition if the connection was lost before the initialization completed.
- Fixed null ref if a connection was closed while a request was in progress.
- Improve socket close management.

2.1.2
-----
Fixed
*****
- Get the socket localEndpoint at startup so that it can be accessed for cleanup after close.

2.1.1
-----
Fixed
*****
- Don't throw a NullReferenceException when a request context is cancelled when it was already complete.

2.1.0.1
-------
Changed
*******
- Changed log message when interface already bound to something more useful.
2.1.0
-----
Added
*****
- ConnectedPeers returns the list of remote endpoints connected to the peer.
- Add IsDisposed property to TcpRequest.
- Added Cancellation support to TcpRequest. A request is cancelled when the TcpRequest object is disposed.

Changed
*******
- Don't complete the requests reader & writer pipes on Dispose.

1.0.3
-----
Changed
*******
- Don't error on disconnection
- Don't keep requests in pendingResponse object.

1.0.1.2
-------
Added
*****
- Added TcpPeer implementation.
- Added Package icon.




