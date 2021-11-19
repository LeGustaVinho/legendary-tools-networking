using System;
using System.Collections.Generic;
using System.Net;
using UnityEngine;

namespace LegendaryTools.Networking
{
    public class Server
    {
        public event Action<Player> OnPlayerConnected;
        public event Action<Player> OnPlayerDisconnected;
        public event Action OnServerInitialized;
        public event Action<Buffer, Player> OnMessageReceived;
        
        public List<Player> PlayerList => new List<Player>(playerList.ToArray());
        
        internal protected readonly UdpProtocol UdpProtocol = new UdpProtocol("Server");
        internal protected readonly TcpProtocol TcpProtocol = new TcpProtocol();
        
        protected readonly List<Player> playerList = new List<Player>();
        protected Dictionary<int,Player> playerTableById = new Dictionary<int,Player>();
        protected Dictionary<IPEndPoint,Player> playerTableByEndPoint = new Dictionary<IPEndPoint,Player>();

        private int clientUdpPort;

        public Server()
        {
            UdpProtocol.OnPacketReceived += OnUdpPacketReceived;
            TcpProtocol.OnListenerPacketReceived += OnListenerPacketReceived;
            TcpProtocol.OnTcpClientConnect += OnTcpClientConnect;
            TcpProtocol.OnTcpClientDisconnect += OnTcpClientDisconnect;
        }

        ~Server()
        {
            UdpProtocol.OnPacketReceived -= OnUdpPacketReceived;
            TcpProtocol.OnListenerPacketReceived -= OnListenerPacketReceived;
            TcpProtocol.OnTcpClientConnect -= OnTcpClientConnect;
            TcpProtocol.OnTcpClientDisconnect -= OnTcpClientDisconnect;
        }

        public void Start(int udpPort, int tcpPort, int clientUdpPort)
        {
            this.clientUdpPort = clientUdpPort;
            bool udpStarted = UdpProtocol.Start(udpPort);
            bool tcpStarted = TcpProtocol.StartListener(tcpPort);

            if (udpStarted && tcpStarted)
            {
                OnServerInitialized?.Invoke();
                OnServerInitialize();
            }
        }

        public void Stop()
        {
            UdpProtocol.Stop();
            TcpProtocol.StopListener();
            TcpProtocol.Disconnect();
        }
        
        public void SendUnreliable(int clientId, Buffer buffer)
        {
            TcpProtocol tcpClient = TcpProtocol.GetClient(clientId);
            UdpProtocol.Send(buffer, new IPEndPoint(IPAddress.Parse(tcpClient.Address), clientUdpPort));
        }

        public void SendReliable(int clientId, Buffer buffer)
        {
            TcpProtocol.SendToClientById(clientId, buffer);
        }

        public void SendMessage(NetworkMessage message, Player player, bool reliable)
        {
            if (reliable)
            {
                SendReliable(player.Id, message.Serialize());
            }
            else
            {
                SendUnreliable(player.Id, message.Serialize());
            }
        }

        public void BroadcastMessage(NetworkMessage message, bool reliable)
        {
            foreach (Player player in playerList)
            {
                SendMessage(message, player, reliable);
            }
        }

        public void KickPlayer(Player player)
        {
            TcpProtocol.KickClient(player.Id);
        }

        public void Update()
        {
            UdpProtocol.Update();
            TcpProtocol.UpdateListener();
            TcpProtocol.UpdateClient();
        }

        protected virtual void OnServerInitialize()
        {
            
        }
        
        protected virtual void OnMessageReceive(Buffer buffer, Player player)
        {
            
        }

        protected virtual void OnPlayerConnect(Player player)
        {
            
        }
        
        protected virtual void OnPlayerDisconnect(Player player)
        {
            
        }
        
        private void OnTcpClientConnect(TcpProtocol tcpProtocol)
        {
            Player newPlayer = new Player(this,
                new IPEndPoint(IPAddress.Parse(tcpProtocol.Ip), clientUdpPort));
            
            playerList.Add(newPlayer);
            playerTableById.Add(newPlayer.Id, newPlayer);
            playerTableByEndPoint.Add(tcpProtocol.EndPoint, newPlayer);
            
            OnPlayerConnected?.Invoke(newPlayer);
            OnPlayerConnect(newPlayer);
        }

        private void OnTcpClientDisconnect(TcpProtocol tcpProtocol)
        {
            Player found = playerList.Find(item => item.TcpProtocol == tcpProtocol);
            if (found != null)
            {
                playerList.Remove(found);
                playerTableById.Remove(found.Id);
                playerTableByEndPoint.Remove(found.TcpProtocol.EndPoint);
                
                OnPlayerDisconnected?.Invoke(found);
                OnPlayerDisconnect(found);
            }
        }
        
        private void OnUdpPacketReceived(Buffer buffer, IPEndPoint source)
        {
            if (playerTableByEndPoint.TryGetValue(source, out Player player))
            {
                OnMessageReceived?.Invoke(buffer, player);
                OnMessageReceive(buffer, player);
            }
        }

        private void OnListenerPacketReceived(Buffer buffer, IPEndPoint source)
        {
            Packet packetType = buffer.PeekPacket();

            if (!playerTableByEndPoint.TryGetValue(source, out Player player))
            {
                Debug.LogError("Player not found");
                return;
            }

            switch (packetType)
            {
                case Packet.PlayerLayer:
                {
                    player.Name = buffer.Deserialize<NetworkMessagePlayerName>().Name;
                    break;
                }
            }
            

            OnMessageReceived?.Invoke(buffer, player);
            OnMessageReceive(buffer, player);
        }
    }
}