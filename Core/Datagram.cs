using System.Net;

namespace LegendaryTools.Networking
{
    /// <summary>
    /// Simple datagram container -- contains a data buffer and the address of where it came from (or where it's going).
    /// </summary>
    public struct Datagram
    {
        public Buffer buffer;
        public IPEndPoint ip;
    }
}