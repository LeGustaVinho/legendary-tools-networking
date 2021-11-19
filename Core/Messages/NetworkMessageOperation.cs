namespace LegendaryTools.Networking
{
    public abstract class NetworkMessageOperation : NetworkMessage
    {
        public const int OPERATION_ID_OFFSET = PACKET_OFFSET + SIZEOF_PACKET;
        public const int SIZEOF_OPERATION_ID = sizeof(ushort);
        
        public ushort OperationId;  //2
        protected override Buffer BeginSerialize()
        {
            Buffer buffer = base.BeginSerialize();
            buffer.Write(OperationId);
            return buffer;
        }
        
        protected override void BeginDeserialize(Buffer buffer)
        {
            base.BeginDeserialize(buffer);
            OperationId = buffer.ReadUInt16();
        }
    }

    public class EmptyCommand : NetworkMessageOperation
    {
        protected override void SerializeBody(Buffer buffer)
        {
        }

        protected override void DeserializeBody(Buffer buffer)
        {
        }
    }
}