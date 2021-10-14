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
        public string Name = "Guest";

        public TcpProtocol TcpProtocol { get; private set; }
        public IPEndPoint UdpAddress { get; private set; }
        
        public Player(TcpProtocol tcpProtocol, IPEndPoint udpAddress)
        {
            this.TcpProtocol = tcpProtocol;
            this.UdpAddress = udpAddress;
        }
    }
}