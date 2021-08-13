using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LegendaryTools;
using LegendaryTools.Networking;
using System.IO;
using System.Net;

public class TransportLayerDemo : MonoBehaviour 
{
    public bool isServer;
    public bool isReliable;
    public int stringSize = 1;
    public int TCPPort;
    public int UDPPort;
    public string ServerExternalIP;
    public string ServerInternalIP;

    UdpProtocol UdpProtocol = new UdpProtocol();
    TcpProtocol TcpProtocol = new TcpProtocol();
    UPnP upnp = new UPnP();

    private void OnPacketReceived(Buffer buffer, IPEndPoint source)
    {
        BinaryReader reader = buffer.BeginReading();
        Packet response = (Packet)reader.ReadByte();

        //if (response == Packet.Empty)
            OnMessageReceived(reader.ReadString());

        buffer.Recycle();
    }

    string BigString()
    {
        string result = string.Empty;

        for (int i = 0; i < stringSize; i++)
            result += "A";

        return result;
    }

    public void Send()
    {
        string message = isServer ? "I am server. " + BigString() : "I am client. " + BigString();

        Buffer buffer = Buffer.Create();
        BinaryWriter writer = buffer.BeginPacket(Packet.Empty);
        writer.Write(message);
        buffer.EndPacket();

        Debug.Log("Buffer size: " + buffer.Size);

        if (!isReliable)
			UdpProtocol.Send(buffer, NetworkUtility.ResolveEndPoint(ServerExternalIP, UDPPort));
        else
        {
            if (!isServer)
                TcpProtocol.SendTcpPacket(buffer);
            else
                TcpProtocol.SendToClient(0, buffer);
        }
    }

    public void ConnectTCP()
    {
        if (!isServer)
        {
            TcpProtocol.OnClientPacketReceived += OnPacketReceived;
            TcpProtocol.Connect(NetworkUtility.ResolveEndPoint(ServerExternalIP, TCPPort), NetworkUtility.ResolveEndPoint(ServerInternalIP, TCPPort));
        }
    }

    public void HostTCP()
    {
        if (isServer)
        {
            TcpProtocol.OnListenerPacketReceived += OnPacketReceived;
            TcpProtocol.StartListener(TCPPort);
            upnp.OpenTCP(TCPPort);
        }
    }

    public void HostUDP()
    {
        UdpProtocol.OnPacketReceived += OnPacketReceived;
        UdpProtocol.Start(UDPPort);
        upnp.OpenUDP(UDPPort);
    }

    void OnMessageReceived(string message)
    {
        Debug.Log("OnMessageReceived: " + message);
    }

    void Update()
    {
        //TCP
        //TcpProtocol.UpdateListener();
        //TcpProtocol.UpdateClient();
    }

    void OnDestroy()
    {
        TcpProtocol.Disconnect();
        UdpProtocol.Stop();
        upnp.Close();
    }

    void OnApplicationQuit()
    {
        TcpProtocol.Disconnect();
        UdpProtocol.Stop();
        upnp.Close();

        Debug.Log("Application ending after " + Time.time + " seconds");
    }
}