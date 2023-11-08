using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable.Requests
{
    internal class RequestFrameWriter(RequestConnectionContext context)
    {
        private readonly RequestConnectionContext _context = context;
        private readonly object _writeLock = new object();

        private bool _completed = false;

        public ValueTask<FlushResult> WriteGoAwayAsync(string errorId)
        {
            lock (_writeLock)
            {
                if (_completed)
                {
                    return default;
                }

            }

            throw new NotImplementedException();
           
        }

        public ValueTask<FlushResult> WriteRequestStartAsync()
        {
            lock (_writeLock)
            {
                if (_completed)
                {
                    return default;
                }

            }
            throw new NotImplementedException();
        }

        public ValueTask<FlushResult> WriteRequestCompleteAsync()
        {
            lock (_writeLock)
            {
                if (_completed)
                {
                    return default;
                }

            }
            throw new NotImplementedException();
        }

        public ValueTask<FlushResult> WriteRequestKeepAliveAsync()
        {
            lock (_writeLock)
            {
                if (_completed)
                {
                    return default;
                }

            }
            throw new NotImplementedException();
        }
    }
}
