using System.IO;

namespace LegendaryTools.Networking
{
    public abstract class NetworkMessage<T>
        where T : NetworkMessage<T>, new()
    {
        public Packet PacketType { get; set; }

        protected virtual Buffer BeginSerialize()
        {
            Buffer buffer = Buffer.CreatePackage(PacketType, out BinaryWriter writer);
            return buffer;
        }

        public abstract void SerializeBody(Buffer buffer);

        public virtual Buffer Serialize()
        {
            Buffer buffer = BeginSerialize();
            SerializeBody(buffer);
            buffer.EndPacket();
            return buffer;
        }

        protected virtual T BeginDeserialize(Buffer buffer)
        {
            buffer.BeginReading();
            T instance = new T {PacketType = (Packet) buffer.ReadByte()};
            return instance;
        }
        
        public abstract void DeserializeBody(Buffer buffer, T instance);

        public T Deserialize(Buffer buffer)
        {
            T instance = BeginDeserialize(buffer);
            DeserializeBody(buffer, instance);
            buffer.Recycle();
            return instance;
        }
    }

    public class StringNetworkMessage : NetworkMessage<StringNetworkMessage>
    {
        public string Message;

        public override void SerializeBody(Buffer buffer)
        {
            buffer.Write(Message);
        }

        public override void DeserializeBody(Buffer buffer, StringNetworkMessage instance)
        {
            instance.Message = buffer.ReadString();
        }
    }
}