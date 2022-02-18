namespace LegendaryTools.Networking
{
    public class NetworkMessagePlayerLayer : NetworkMessage
    {
        public ushort Layer;
        
        public NetworkMessagePlayerLayer()
        {
            PacketType = Packet.PlayerLayer;
        }

        public NetworkMessagePlayerLayer(ushort layer)
        {
            Layer = layer;
        }
        
        protected override void SerializeBody(Buffer buffer)
        {
            buffer.Write(Layer);
        }

        protected override void DeserializeBody(Buffer buffer)
        {
            Layer = buffer.ReadUInt16();
        }
    }
}