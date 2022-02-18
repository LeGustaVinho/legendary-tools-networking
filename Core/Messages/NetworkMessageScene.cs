namespace LegendaryTools.Networking
{
    public class NetworkMessageScene : NetworkMessage
    {
        public string SceneName;

        public NetworkMessageScene()
        {
        }

        public NetworkMessageScene(Packet packetType, string sceneName)
        {
            PacketType = packetType;
            SceneName = sceneName;
        }
        
        protected override void SerializeBody(Buffer buffer)
        {
            buffer.Write(SceneName);
        }

        protected override void DeserializeBody(Buffer buffer)
        {
            SceneName = buffer.ReadString();
        }
    }
}