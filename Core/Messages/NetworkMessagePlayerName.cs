namespace LegendaryTools.Networking
{
    public class NetworkMessagePlayerName : NetworkMessage
    {
        public string Name;

        public NetworkMessagePlayerName()
        {
        }

        public NetworkMessagePlayerName(string name)
        {
            Name = name;
        }
        
        protected override void SerializeBody(Buffer buffer)
        {
            buffer.Write(Name);
        }

        protected override void DeserializeBody(Buffer buffer)
        {
            Name = buffer.ReadString();
        }
    }
}