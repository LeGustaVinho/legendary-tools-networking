using System.IO;

namespace LegendaryTools.Networking
{
    public abstract class NetworkMessage
    {
        public const int PACKET_OFFSET = Buffer.SIZE_OFFSET + Buffer.SIZEOF_SIZE;
        public const int SIZEOF_PACKET = sizeof(byte);
        
        public Packet PacketType; //1

        protected virtual Buffer BeginSerialize()
        {
            Buffer buffer = Buffer.CreatePackage(PacketType, out BinaryWriter writer);
            return buffer;
        }

        protected abstract void SerializeBody(Buffer buffer);

        public virtual Buffer Serialize()
        {
            Buffer buffer = BeginSerialize();
            SerializeBody(buffer);
            buffer.EndPacket();
            return buffer;
        }

        protected virtual void BeginDeserialize(Buffer buffer)
        {
            buffer.BeginReading();
            PacketType = (Packet) buffer.ReadByte();
        }
        
        protected abstract void DeserializeBody(Buffer buffer);

        public void Deserialize(Buffer buffer)
        {
            BeginDeserialize(buffer);
            DeserializeBody(buffer);
            buffer.Recycle();
        }
    }

    public class StringNetworkMessage : NetworkMessage
    {
        public string Message;

        protected override void SerializeBody(Buffer buffer)
        {
            buffer.Write(Message);
        }

        protected override void DeserializeBody(Buffer buffer)
        {
            Message = buffer.ReadString();
        }
    }
}