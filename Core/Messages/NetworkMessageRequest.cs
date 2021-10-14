namespace LegendaryTools.Networking
{
    public abstract class NetworkMessageRequest<T> : NetworkMessageOperation<T>
        where T : NetworkMessageRequest<T>, new()
    {
        public byte RequestId;
        protected override Buffer BeginSerialize()
        {
            Buffer buffer = base.BeginSerialize();
            buffer.Write(RequestId);
            return buffer;
        }
        
        protected override T BeginDeserialize(Buffer buffer)
        {
            T instance = base.BeginDeserialize(buffer);
            instance.RequestId = buffer.ReadByte();
            return instance;
        }
    }
}