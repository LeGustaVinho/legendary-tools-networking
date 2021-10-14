using System.IO;
using LegendaryTools.Networking;
using UnityEngine;

public class ServerBehaviour : MonoBehaviour
{
    public Server Server = new Server();

    public int TcpListenPort;
    public int UdpListenPort;
    public int ClientUdpPort;

    public bool AutoStart;

    public void Start()
    {
        if (AutoStart)
        {
            Server.Start(UdpListenPort, TcpListenPort, ClientUdpPort);
        }
    }
    
    public void Update()
    {
        Server.Update();

        if (Input.GetKeyUp(KeyCode.K))
        {
            Server.KickPlayer(Server.PlayerList[0]);
        }
        
        if (Input.GetKeyUp(KeyCode.E))
        {
            Buffer buffer = Buffer.CreatePackage(Packet.KeepAlive, out BinaryWriter writer);
            buffer.Write("Error desc");
            buffer.EndPacket();
            
            Server.SendReliable(Server.PlayerList[0].Id, buffer);
        }
    }
    
    public void OnDestroy()
    {
        Server.Stop();
    }
}
