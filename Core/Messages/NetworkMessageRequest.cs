namespace LegendaryTools.Networking
{
    public abstract class NetworkMessageRequest : NetworkMessageOperation
    {
        public const int REQUEST_ID_OFFSET = OPERATION_ID_OFFSET + SIZEOF_OPERATION_ID;
        public const int SIZEOF_REQUEST_ID = sizeof(byte);
        
        internal byte RequestId; //3
        protected override Buffer BeginSerialize()
        {
            Buffer buffer = base.BeginSerialize();
            buffer.Write(RequestId);
            return buffer;
        }
        
        protected override void BeginDeserialize(Buffer buffer)
        {
            base.BeginDeserialize(buffer);
            RequestId = buffer.ReadByte();
        }
    }

    public class EmptyRequest : NetworkMessageRequest
    {
        protected override void SerializeBody(Buffer buffer)
        {
        }

        protected override void DeserializeBody(Buffer buffer)
        {
        }
    }
}