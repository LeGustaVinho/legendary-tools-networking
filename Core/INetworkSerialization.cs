using LegendaryTools.Networking;

namespace LegendaryTools.Networking
{
    public interface INetworkSerialization
    {
        Buffer Serialize();
        void Deserialize(Buffer buffer);
    }
}