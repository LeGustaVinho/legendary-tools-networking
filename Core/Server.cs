using System.Net;

namespace LegendaryTools.Networking
{
    public class Server
    {
        protected readonly UdpProtocol udpProtocol = new UdpProtocol("Server");

        public Server()
        {
            udpProtocol.OnPacketReceived += OnUdpPacketReceived;
        }

        ~Server()
        {
            udpProtocol.OnPacketReceived -= OnUdpPacketReceived;
        }
        
        public void Start(int udpPort)
        {
            udpProtocol.Start(udpPort);
        }

        public void Stop()
        {
            udpProtocol.Stop();
        }
        
        public void Update()
        {
            udpProtocol.Update();
        }

        protected virtual void OnUdpPacketReceived(Buffer buffer, IPEndPoint source)
        {
            
        }
    }
}