using System;
using System.Net;
using System.Threading;
using UnityEngine;

namespace Network.Tests
{
    public class PingPongExample : MonoBehaviour
    {
        // public PingPongClient client = new PingPongClient();
        // public PingPongServer server = new PingPongServer();
        //
        // public string ServerIP = "127.0.0.1";
        // public int ServerPort = 25000;
        //
        // private void Start()
        // {
        //     server.Start(25000);
        //     client.Connect(new IPEndPoint(IPAddress.Parse(ServerIP), ServerPort));
        // }
        //
        // private void OnDestroy()
        // {
        //     server.Stop();
        //     client.Disconnect();
        // }
        //
        // private void Update()
        // {
        //     if (Input.GetKeyUp(KeyCode.A))
        //     {
        //         client.SendMessage("Hi server");
        //     }
        //
        //     //server.Update();
        //     //client.Update();
        // }
    }
}