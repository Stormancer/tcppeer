using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Tcp
{
    internal class Counters
    {
        private Meter _meter;

        public Counters(string name)
        {
            _meter = new Meter($"Stormancer.Tcp.{name}", "1.0.0");
            Sent = _meter.CreateCounter<int>("requestsSent");
            Received = _meter.CreateCounter<int>("requestsReceived");
        }
        public Counter<int> Sent { get; }
        public Counter<int> Received { get; }
    }
}
