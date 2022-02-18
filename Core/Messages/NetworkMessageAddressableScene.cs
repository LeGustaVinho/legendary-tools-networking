namespace LegendaryTools.Networking
{
    public class NetworkMessageAddressableScene : NetworkMessage
    {
        public string AddressableId;

        public NetworkMessageAddressableScene()
        {
        }

        public NetworkMessageAddressableScene(Packet packetType, string addressableId)
        {
            PacketType = packetType;
            AddressableId = addressableId;
        }
        
        protected override void SerializeBody(Buffer buffer)
        {
            buffer.Write(AddressableId);
        }

        protected override void DeserializeBody(Buffer buffer)
        {
            AddressableId = buffer.ReadString();
        }
    }
}