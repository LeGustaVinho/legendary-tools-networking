using System.Net;

namespace LegendaryTools.Networking
{
    /// <summary>
    /// Class containing basic information about a remote player.
    /// </summary>
    public class Player
    {
        /// <summary>
        /// Protocol version.
        /// </summary>
        public const int VERSION = 1;

        /// <summary>
        /// All players have a unique identifier given by the server.
        /// </summary>
        public int Id => TcpProtocol.Id;

        /// <summary>
        /// All players have a name that they chose for themselves.
        /// </summary>
        private string name = "Guest";

        public string Name
        {
            set
            {
                if (Server != null)
                {
                    Server.SendMessage(new NetworkMessagePlayerName(value), this, true);
                    name = value;
                }

                Client?.SendMessage(new NetworkMessagePlayerName(value), true);
            }
            get => name;
        }

        private ushort layer;

        public ushort Layer
        {
            set
            {
                if (Server != null)
                {
                    Server.SendMessage(new NetworkMessagePlayerLayer(value), this, true);
                    Layer = value;
                }
                
            }
            get => layer;
        }

        public Client Client { get; private set; }
        public Server Server { get; private set; }

        public TcpProtocol TcpProtocol =>
            Server != null ? Server.TcpProtocol : Client?.TcpProtocol;
        public IPEndPoint UdpAddress { get; private set; }
        
        public Player(Client client)
        {
            Client = client;
        }
        
        public Player(Server server)
        {
            Server = server;
        }
        
        public Player(Server server, IPEndPoint udpAddress)
        {
            Server = server;
            UdpAddress = udpAddress;
        }
    }
}