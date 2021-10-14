using System;
using System.IO;
using System.Net;
using UnityEngine;

namespace LegendaryTools.Networking
{
    public class Client
    {
        public event Action OnConnectedToServer;
        public event Action OnDisconnectedFromServer;
        public event Action OnFailedToConnect;
        
        
        protected readonly UdpProtocol udpProtocol = new UdpProtocol("Client");
        protected readonly TcpProtocol tcpProtocol = new TcpProtocol();
        
        protected ushort lastAllocatedRequestId;
        
        private IPEndPoint serverUdpAddress;
        private IPEndPoint serverTcpAddress;

        public Client()
        {
            udpProtocol.OnPacketReceived += OnUdpPacketReceived;
            tcpProtocol.OnClientPacketReceived += OnClientPacketReceived;
        }

        ~Client()
        {
            udpProtocol.OnPacketReceived -= OnUdpPacketReceived;
            tcpProtocol.OnClientPacketReceived -= OnClientPacketReceived;
        }
        
        public void Connect(IPEndPoint serverUdpAddress, IPEndPoint serverTcpAddress, int clientUdpListenerPort = 25001)
        {
            this.serverUdpAddress = serverUdpAddress;
            this.serverTcpAddress = serverTcpAddress;
            
            bool udpConnected = udpProtocol.Start(clientUdpListenerPort);
            bool tcpConnected = tcpProtocol.Connect(serverTcpAddress);

            if (udpConnected && tcpConnected)
            {
                OnConnectedToServer?.Invoke();
            }
            else
            {
                OnFailedToConnect?.Invoke();
            }
        }

        public void Disconnect()
        {
            udpProtocol.Stop();
            tcpProtocol.Disconnect();
        }

        public void SendUnreliable(Buffer buffer)
        {
            udpProtocol.Send(buffer, serverUdpAddress);
        }

        public void SendReliable(Buffer buffer)
        {
            tcpProtocol.SendTcpPacket(buffer);
        }
        
        public void SendMessage<T>(NetworkMessage<T> message, bool reliable)
            where T : NetworkMessage<T>, new()
        {
            if (reliable)
            {
                SendReliable(message.Serialize());
            }
            else
            {
                SendUnreliable(message.Serialize());
            }
        }

        public void SendKeepAlive()
        {
            Buffer buffer = Buffer.CreatePackage(Packet.KeepAlive, out BinaryWriter writer);
            buffer.EndPacket();
            SendReliable(buffer);
        }

        public void Update()
        {
            udpProtocol.Update();
            tcpProtocol.UpdateClient();
        }

        protected virtual void OnUdpPacketReceived(Buffer buffer, IPEndPoint source)
        {
        }
        
        protected virtual void OnClientPacketReceived(Buffer buffer, IPEndPoint source)
        {
            buffer.BeginReading();
            byte packId = buffer.ReadByte();
            
            Debug.LogError("Packet ID: " + packId);
        }
    }
}