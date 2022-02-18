using UnityEngine;
using Object = System.Object;

namespace LegendaryTools.Networking
{
    public class NetworkMessageAddressableInstantiate : NetworkMessage
    {
        public uint NetworkIdentity; //2
        public string AddressableId; //3
        public Vector3 Position;
        public Quaternion Rotation;
        public uint Parent; 
        public readonly Object[] Args;

        public NetworkMessageAddressableInstantiate()
        {
            PacketType = Packet.AddressableInstantiate;
        }
        
        public NetworkMessageAddressableInstantiate(uint networkIdentity, 
            string addressableId, 
            Vector3 position,
            Quaternion rotation,
            uint parent,
            params Object[] args) : this()
        {
            NetworkIdentity = networkIdentity;
            AddressableId = addressableId;
            Args = args;
        }

        protected override void SerializeBody(Buffer buffer)
        {
            buffer.Write(NetworkIdentity);
            buffer.Write(AddressableId);
            
            buffer.Write(Position);
            buffer.Write(Rotation);
            buffer.Write(Parent);

            foreach (object arg in Args)
            {
                buffer.Write(arg);
            }
        }

        protected override void DeserializeBody(Buffer buffer)
        {
            NetworkIdentity = buffer.ReadUInt32();
            AddressableId = buffer.ReadString();

            Position = buffer.ReadVector3();
            Rotation = buffer.ReadQuaternion();
            Parent = buffer.ReadUInt32();
        }
    }
}