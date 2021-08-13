using System.Net;
using System.Threading;
using LegendaryTools.Networking;
using UnityEngine;

namespace Network.Tests
{
    public class PingPongServer : Server
    {
        protected override void OnUdpPacketReceived(Buffer buffer, IPEndPoint source)
        {
            buffer.BeginReading(0);
            udpProtocol.Send(buffer, source);
        }
    }
}