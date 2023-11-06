using Stormancer.Networking.Reliable;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Tcp.Tests
{
    public static class ConnectionSuccessMiddlewareExtensions
    {
        public static TransportsOptions UseConnectionSuccessMiddleware(this TransportsOptions options,ConnectionSuccessNotifier notifier)
        {
            options.Use(notifier.OnConnection);
            return options;
        }
    }
    public class ConnectionSuccessNotifier
    {
        private readonly TaskCompletionSource _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ConnectionDelegate OnConnection(ConnectionDelegate next)
        {

            return async (NetworkConnection connection) =>
            {
                _tcs.SetResult();
                await next(connection);
            };
        }

        public Task WaitAsync(TimeSpan timeSpan)=> _tcs.Task.WaitAsync(timeSpan);
    }
}
