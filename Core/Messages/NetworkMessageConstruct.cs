using System;

namespace LegendaryTools.Networking
{
    public class NetworkMessageConstruct : NetworkMessage
    {
        public uint NetworkIdentity; //2
        public string AssemblyQualifiedName; //3
        public readonly Object[] Args;

        public NetworkMessageConstruct()
        {
            PacketType = Packet.Construct;
        }
        
        public NetworkMessageConstruct(uint networkIdentity, string assemblyQualifiedName, params Object[] args) : this()
        {
            NetworkIdentity = networkIdentity;
            AssemblyQualifiedName = assemblyQualifiedName;
            Args = args;
        }

        public INetworkObject CreateInstance()
        {
            Type type = Type.GetType(AssemblyQualifiedName);

            if (type != null)
            {
                if (type.Implements(typeof(INetworkObject)))
                {
                    INetworkObject networkObject = Activator.CreateInstance(type) as INetworkObject;
                    return networkObject;
                }
            }

            return null;
        }
        
        protected override void SerializeBody(Buffer buffer)
        {
            buffer.Write(NetworkIdentity);
            buffer.Write(AssemblyQualifiedName);

            foreach (object arg in Args)
            {
                buffer.Write(arg);
            }
        }

        protected override void DeserializeBody(Buffer buffer)
        {
            NetworkIdentity = buffer.ReadUInt32();
            AssemblyQualifiedName = buffer.ReadString();
        }
    }
}