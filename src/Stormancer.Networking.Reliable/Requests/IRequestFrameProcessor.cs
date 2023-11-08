using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable.Requests
{
    internal interface IRequestFrameProcessor
    {
        void ProcessFrame(RequestFrame frame);
    }
}
