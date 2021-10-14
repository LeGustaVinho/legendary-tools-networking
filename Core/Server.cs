using System;
using System.Collections.Generic;
using System.Net;

namespace LegendaryTools.Networking
{
    public class Server
    {
        public event Action<Player> OnPlayerConnected;
        public event Action<Player> OnPlayerDisconnected;
        public event Action OnServerInitialized;
        
        public List<Player> PlayerList => new List<Player>(playerList.ToArray());
        
        protected readonly UdpProtocol udpProtocol = new UdpProtocol("Server");
        protected readonly TcpProtocol tcpProtocol = new TcpProtocol();
        
        protected readonly List<Player> playerList = new List<Player>();
        protected Dictionary<int,Player> playerTableById = new Dictionary<int,Player>();
        protected Dictionary<IPEndPoint,Player> playerTableByEndPoint = new Dictionary<IPEndPoint,Player>();

        private int clientUdpPort;

        public Server()
        {
            udpProtocol.OnPacketReceived += OnUdpPacketReceived;
            tcpProtocol.OnListenerPacketReceived += OnListenerPacketReceived;
            tcpProtocol.OnTcpClientConnect += OnTcpClientConnect;
            tcpProtocol.OnTcpClientDisconnect += OnTcpClientDisconnect;
        }

        ~Server()
        {
            udpProtocol.OnPacketReceived -= OnUdpPacketReceived;
            tcpProtocol.OnListenerPacketReceived -= OnListenerPacketReceived;
            tcpProtocol.OnTcpClientConnect -= OnTcpClientConnect;
            tcpProtocol.OnTcpClientDisconnect -= OnTcpClientDisconnect;
        }

        public void Start(int udpPort, int tcpPort, int clientUdpPort)
        {
            this.clientUdpPort = clientUdpPort;
            bool udpStarted = udpProtocol.Start(udpPort);
            bool tcpStarted = tcpProtocol.StartListener(tcpPort);

            if (udpStarted && tcpStarted)
            {
                OnServerInitialized?.Invoke();    
            }
        }

        public void Stop()
        {
            udpProtocol.Stop();
            tcpProtocol.StopListener();
            tcpProtocol.Disconnect();
        }
        
        public void SendUnreliable(int clientId, Buffer buffer)
        {
            TcpProtocol tcpClient = tcpProtocol.GetClient(clientId);
            udpProtocol.Send(buffer, new IPEndPoint(IPAddress.Parse(tcpClient.Address), clientUdpPort));
        }

        public void SendReliable(int clientId, Buffer buffer)
        {
            tcpProtocol.SendToClientById(clientId, buffer);
        }

        public void SendMessage<T>(NetworkMessage<T> message, Player player, bool reliable)
            where T : NetworkMessage<T>, new()
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

        public void BroadcastMessage<T>(NetworkMessage<T> message, bool reliable)
            where T : NetworkMessage<T>, new()
        {
            foreach (Player player in playerList)
            {
                SendMessage(message, player, reliable);
            }
        }

        public void KickPlayer(Player player)
        {
            tcpProtocol.KickClient(player.Id);
        }

        public void Update()
        {
            udpProtocol.Update();
            tcpProtocol.UpdateListener();
            tcpProtocol.UpdateClient();
        }

        protected virtual void OnUdpPacketReceived(Buffer buffer, IPEndPoint source)
        {
        }

        protected virtual void OnListenerPacketReceived(Buffer buffer, IPEndPoint source)
        {
        }
        
        protected virtual void OnTcpClientConnect(TcpProtocol tcpProtocol)
        {
            Player newPlayer = new Player(tcpProtocol,
                new IPEndPoint(IPAddress.Parse(tcpProtocol.Ip), clientUdpPort));
            
            playerList.Add(newPlayer);
            playerTableById.Add(newPlayer.Id, newPlayer);
            playerTableByEndPoint.Add(tcpProtocol.EndPoint, newPlayer);
            
            OnPlayerConnected?.Invoke(newPlayer);
        }

        protected virtual void OnTcpClientDisconnect(TcpProtocol tcpProtocol)
        {
            Player found = playerList.Find(item => item.TcpProtocol == tcpProtocol);
            if (found != null)
            {
                playerList.Remove(found);
                playerTableById.Remove(found.Id);
                playerTableByEndPoint.Remove(found.TcpProtocol.EndPoint);
                
                OnPlayerDisconnected?.Invoke(found);
            }
        }
    }
}