using System;
using System.Collections.Generic;
using System.IO;
using LegendaryTools.Networking;
using UnityEngine;
using Buffer = LegendaryTools.Networking.Buffer;

[Serializable]
public class Character : NetworkObject
{
    public string Name;
    
    public override void OnInitialize(Buffer buffer)
    {
        Name = buffer.ReadString();
    }
}

public class ServerBehaviour : MonoBehaviour
{
    public Server Server = new Server();

    public int TcpListenPort;
    public int UdpListenPort;
    public int ClientUdpPort;

    public bool AutoStart;

    public List<Character> Character = new List<Character>();

    public void Start()
    {
        Server.OnMessageReceived += OnMessageReceived;
        
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

        if (Input.GetKeyUp(KeyCode.J))
        {
            Character.Add(Server.Construct<Character>(0,"Gustavo"));
        }
    }

    public void OnDestroy()
    {
        Server.OnMessageReceived -= OnMessageReceived;
        Server.Stop();
    }
    
    void OnMessageReceived(Buffer buffer, Player player)
    {
        Packet packet = buffer.PeekPacket();

        switch (packet)
        {
            case Packet.RequestMessage:
            {
                ushort operationId = buffer.PeekUInt16(NetworkMessageOperation.OPERATION_ID_OFFSET);

                switch (operationId)
                {
                    case 1:
                    {
                        EmptyRequest request = buffer.Deserialize<EmptyRequest>();
                        
                        EmptyResponse response = new EmptyResponse()
                        {
                            PacketType = Packet.RequestMessage,
                            OperationId = 2,
                            ResponseId = request.RequestId
                        };
                        
                        Server.SendMessage(response, player, true);
                        
                        break;
                    }
                }
                
                break;
            }
        }
    }
}
