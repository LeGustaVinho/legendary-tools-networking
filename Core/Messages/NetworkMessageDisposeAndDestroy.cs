namespace LegendaryTools.Networking
{
    public class NetworkMessageDisposeAndDestroy : NetworkMessage
    {
        public uint NetworkIdentity; //2

        public NetworkMessageDisposeAndDestroy()
        {
            PacketType = Packet.DisposeAndDestroy;
        }
        
        public NetworkMessageDisposeAndDestroy(uint networkIdentity) : this()
        {
            NetworkIdentity = networkIdentity;
        }

        protected override void SerializeBody(Buffer buffer)
        {
            buffer.Write(NetworkIdentity);
        }

        protected override void DeserializeBody(Buffer buffer)
        {
            NetworkIdentity = buffer.ReadUInt32();
        }
    }
}