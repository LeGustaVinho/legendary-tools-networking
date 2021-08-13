using System.Net;

namespace LegendaryTools.Networking
{
    public class Client
    {
        private readonly UdpProtocol udpProtocol = new UdpProtocol("Client");
        private IPEndPoint serverUdp;

        public Client()
        {
            udpProtocol.OnPacketReceived += OnUdpPacketReceived;
        }
        
        ~Client()
        {
            udpProtocol.OnPacketReceived -= OnUdpPacketReceived;
        }
        
        public void Connect(IPEndPoint serverUdp, int clientUdpListenerPort = 25001)
        {
            this.serverUdp = serverUdp;
            udpProtocol.Start(clientUdpListenerPort);
        }

        public void Disconnect()
        {
            udpProtocol.Stop();
        }

        public void SendUnreliable(Buffer buffer)
        {
            udpProtocol.Send(buffer, serverUdp);
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