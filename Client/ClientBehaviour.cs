using System.Collections;
using LegendaryTools.Networking;
using UnityEngine;

public class ClientBehaviour : MonoBehaviour
{
    public Client Client = new Client();

    public string ServerTcpAddress;
    public int ServerTcpPort;
    
    public string ServerUdpAddress;
    public int ServerUdpPort;
    public int ClientListenPort;

    public bool AutoConnect;
    
    public IEnumerator Start()
    {
        yield return new WaitForSeconds(1);
        
        if (AutoConnect)
        {
            Client.Connect(NetworkUtility.ResolveEndPoint(ServerUdpAddress, ServerUdpPort), 
                NetworkUtility.ResolveEndPoint(ServerTcpAddress, ServerTcpPort), 
                ClientListenPort);
        }
    }

    public void Update()
    {
        Client.Update();
        
        if (Input.GetKeyUp(KeyCode.L))
        {
            Client.SendKeepAlive();
        }
    }

    public void OnDestroy()
    {
        Client.Disconnect();
    }
}
