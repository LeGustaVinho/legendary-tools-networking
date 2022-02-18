using UnityEngine;
using Object = System.Object;

namespace LegendaryTools.Networking
{
    public class NetworkMessageResourceInstantiate : NetworkMessage
    {
        public uint NetworkIdentity; //2
        public string Path; //3
        public Vector3 Position;
        public Quaternion Rotation;
        public uint Parent; 
        public readonly Object[] Args;

        public NetworkMessageResourceInstantiate()
        {
            PacketType = Packet.ResourcesInstantiate;
        }
        
        public NetworkMessageResourceInstantiate(uint networkIdentity, 
            string path, 
            Vector3 position,
            Quaternion rotation,
            uint parent,
            params Object[] args) : this()
        {
            NetworkIdentity = networkIdentity;
            Path = path;
            Args = args;
        }

        protected override void SerializeBody(Buffer buffer)
        {
            buffer.Write(NetworkIdentity);
            buffer.Write(Path);
            
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
            Path = buffer.ReadString();

            Position = buffer.ReadVector3();
            Rotation = buffer.ReadQuaternion();
            Parent = buffer.ReadUInt32();
        }
    }
}