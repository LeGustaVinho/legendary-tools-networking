using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace LegendaryTools.Networking
{
    public class Client
    {
        public event Action OnConnectedToServer;
        public event Action OnDisconnectedFromServer;
        public event Action OnFailedToConnect;
        public event Action<Buffer> OnMessageReceived;
        
        public Player LocalPlayer { private set; get; }
        
        internal protected readonly UdpProtocol UdpProtocol = new UdpProtocol("Client");
        internal protected readonly TcpProtocol TcpProtocol = new TcpProtocol();
        
        protected byte lastAllocatedRequestId;
        protected readonly Dictionary<byte, Action<Buffer>> pendingResponse = new Dictionary<byte, Action<Buffer>>(); 
        
        private IPEndPoint serverUdpAddress;
        private IPEndPoint serverTcpAddress;
        
        public Client()
        {
            UdpProtocol.OnPacketReceived += OnUdpPacketReceived;
            TcpProtocol.OnClientPacketReceived += OnClientPacketReceived;
            TcpProtocol.OnTcpSocketClose += OnTcpSocketClose;
        }

        ~Client()
        {
            UdpProtocol.OnPacketReceived -= OnUdpPacketReceived;
            TcpProtocol.OnClientPacketReceived -= OnClientPacketReceived;
            TcpProtocol.OnTcpSocketClose -= OnTcpSocketClose;
        }
        
        public void Connect(IPEndPoint serverUdpAddress, IPEndPoint serverTcpAddress, int clientUdpListenerPort = 25001)
        {
            this.serverUdpAddress = serverUdpAddress;
            this.serverTcpAddress = serverTcpAddress;
            
            bool udpConnected = UdpProtocol.Start(clientUdpListenerPort);
            bool tcpConnected = TcpProtocol.Connect(serverTcpAddress);

            if (udpConnected && tcpConnected)
            {
                LocalPlayer = new Player(this);
                OnConnectedToServer?.Invoke();
                OnConnectToServer();
            }
            else
            {
                OnFailedToConnect?.Invoke();
                OnFailToConnect();
            }
        }

        public void Disconnect()
        {
            UdpProtocol.Stop();
            TcpProtocol.Disconnect();
            LocalPlayer = null;
        }

        public void SendUnreliable(Buffer buffer)
        {
            UdpProtocol.Send(buffer, serverUdpAddress);
        }

        public void SendReliable(Buffer buffer)
        {
            TcpProtocol.SendTcpPacket(buffer);
        }
        
        public void SendMessage(NetworkMessage message, bool reliable)
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

        public void SendRequest(NetworkMessageRequest message, Action<Buffer> responseCallback)
        {
            lock (pendingResponse)
            {
                message.RequestId = AllocateRequestID();
                pendingResponse.Add(message.RequestId, responseCallback);
            }

            SendMessage(message, true);
        }
        
        public void SendKeepAlive()
        {
            Buffer buffer = Buffer.CreatePackage(Packet.KeepAlive, out BinaryWriter writer);
            buffer.EndPacket();
            SendReliable(buffer);
        }

        public void Update()
        {
            UdpProtocol.Update();
            TcpProtocol.UpdateClient();
        }

        protected virtual void OnConnectToServer()
        {
            
        }
        
        protected virtual void OnDisconnectToServer()
        {
            
        }
        
        protected virtual void OnFailToConnect()
        {
            
        }
        
        protected virtual void OnMessageReceive(Buffer buffer)
        {
            
        }

        private void OnUdpPacketReceived(Buffer buffer, IPEndPoint source)
        {
            OnMessageReceived?.Invoke(buffer);
            OnMessageReceive(buffer);
        }

        private void OnClientPacketReceived(Buffer buffer, IPEndPoint source)
        {
            Packet packetType = buffer.PeekPacket();

            switch (packetType)
            {
                case Packet.Disconnect:
                {
                    OnDisconnectedFromServer?.Invoke();
                    break;
                }
                case Packet.ResponseMessage:
                {
                    byte response = buffer.PeekByte(NetworkMessageResponse.RESPONSE_ID_OFFSET);
                    if (pendingResponse.TryGetValue(response, out Action<Buffer> responseCallback))
                    {
                        pendingResponse.Remove(response);
                        responseCallback.Invoke(buffer);
                    }
                    buffer.Recycle();
                    
                    break;
                }
                case Packet.PlayerLayer:
                {
                    LocalPlayer.Name = buffer.Deserialize<NetworkMessagePlayerName>().Name;
                    break;
                }
                case Packet.PlayerName:
                {
                    LocalPlayer.Name = buffer.Deserialize<NetworkMessagePlayerName>().Name;
                    break;
                }
            }
            
            OnMessageReceived?.Invoke(buffer);
            OnMessageReceive(buffer);
        }
        
        private void OnTcpSocketClose()
        {
            LocalPlayer = null;
            OnDisconnectedFromServer?.Invoke();
            OnDisconnectToServer();
        }

        private byte AllocateRequestID()
        {
            byte result = lastAllocatedRequestId++;
            if (lastAllocatedRequestId == byte.MaxValue)
            {
                lastAllocatedRequestId = 0;
            }
            return result;
        }
    }
}