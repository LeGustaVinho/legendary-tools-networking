namespace LegendaryTools.Networking
{
    public abstract class NetworkMessageResponse : NetworkMessageOperation
    {
        public const int RESPONSE_ID_OFFSET = OPERATION_ID_OFFSET + SIZEOF_OPERATION_ID;
        public const int SIZEOF_RESPONSE_ID = sizeof(byte);
        
        internal byte ResponseId; //3
        protected override Buffer BeginSerialize()
        {
            Buffer buffer = base.BeginSerialize();
            buffer.Write(ResponseId);
            return buffer;
        }
        
        protected override void BeginDeserialize(Buffer buffer)
        {
            base.BeginDeserialize(buffer);
            ResponseId = buffer.ReadByte();
        }
    }

    public class EmptyResponse : NetworkMessageResponse
    {
        protected override void SerializeBody(Buffer buffer)
        {
        }

        protected override void DeserializeBody(Buffer buffer)
        {
        }
    }
}