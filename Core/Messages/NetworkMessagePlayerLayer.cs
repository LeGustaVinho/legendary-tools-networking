namespace LegendaryTools.Networking
{
    public class NetworkMessagePlayerLayer : NetworkMessage
    {
        public ushort Layer;
        
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