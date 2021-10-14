namespace LegendaryTools.Networking
{
    public abstract class NetworkMessageOperation<T> : NetworkMessage<T>
        where T : NetworkMessageOperation<T>, new()
    {
        public ushort OperationId;
        protected override Buffer BeginSerialize()
        {
            Buffer buffer = base.BeginSerialize();
            buffer.Write(OperationId);
            return buffer;
        }
        
        protected override T BeginDeserialize(Buffer buffer)
        {
            T instance = base.BeginDeserialize(buffer);
            instance.OperationId = buffer.ReadUInt16();
            return instance;
        }
    }
}