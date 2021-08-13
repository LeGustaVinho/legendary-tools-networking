using System.IO;
using System.Net;
using LegendaryTools.Networking;
using UnityEngine;

namespace Network.Tests
{
    public class PingPongClient : Client
    {
        public void SendMessage(string msg)
        {
            Buffer buffer = Buffer.Create();
            BinaryWriter writer = buffer.BeginPacket(Packet.Empty);
            writer.Write(msg);
            buffer.EndPacket();
            Debug.Log("[Client] -> Sent: " + msg);
            SendUnreliable(buffer);
        }

        protected override void OnUdpPacketReceived(Buffer buffer, IPEndPoint source)
        {
            BinaryReader reader = buffer.BeginReading();
            
            Packet response = (Packet)reader.ReadByte();
            Debug.Log("[Server] -> Answer: " + reader.ReadString());

            buffer.Recycle();
        }
    }
}