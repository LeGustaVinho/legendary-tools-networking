using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace LegendaryTools.Networking
{
    public class Server
    {
        public event Action<Player> OnPlayerConnected;
        public event Action<Player> OnPlayerDisconnected;
        public event Action OnServerInitialized;
        public event Action<Buffer, Player> OnMessageReceived;

        public List<Player> PlayerList => new List<Player>(playerList.ToArray());

        private readonly UdpProtocol UdpProtocol = new UdpProtocol("Server");
        protected internal readonly TcpProtocol TcpProtocol = new TcpProtocol();

        protected readonly List<Player> playerList = new List<Player>();
        protected Dictionary<int, Player> playerTableById = new Dictionary<int, Player>();
        protected readonly Bictionary<IPEndPoint, Player> playerTableByEndPoint = new Bictionary<IPEndPoint, Player>();
        protected readonly Dictionary<uint, INetworkObject> networkObjects = new Dictionary<uint, INetworkObject>();
        protected readonly Dictionary<string, AsyncOperation> pendingScenes = new Dictionary<string, AsyncOperation>();

        protected readonly Dictionary<string, AsyncOperationHandle<SceneInstance>> pendingAddressablesScenes =
            new Dictionary<string, AsyncOperationHandle<SceneInstance>>();
        protected readonly Dictionary<string, AsyncOperationHandle<SceneInstance>> loadedAddressablesScenes =
            new Dictionary<string, AsyncOperationHandle<SceneInstance>>();

        private int clientUdpPort;

        private uint lastAllocatedNetworkIdentity;
        private readonly object lastAllocatedNetworkIdentityObj = new object();

        public Server()
        {
            UdpProtocol.OnPacketReceived += OnUdpPacketReceived;
            TcpProtocol.OnListenerPacketReceived += OnListenerPacketReceived;
            TcpProtocol.OnTcpClientConnect += OnTcpClientConnect;
            TcpProtocol.OnTcpClientDisconnect += OnTcpClientDisconnect;
        }

        ~Server()
        {
            UdpProtocol.OnPacketReceived -= OnUdpPacketReceived;
            TcpProtocol.OnListenerPacketReceived -= OnListenerPacketReceived;
            TcpProtocol.OnTcpClientConnect -= OnTcpClientConnect;
            TcpProtocol.OnTcpClientDisconnect -= OnTcpClientDisconnect;
        }

        public void Start(int udpPort, int tcpPort, int clientUdpPort)
        {
            this.clientUdpPort = clientUdpPort;
            bool udpStarted = UdpProtocol.Start(udpPort);
            bool tcpStarted = TcpProtocol.StartListener(tcpPort);

            if (udpStarted && tcpStarted)
            {
                OnServerInitialized?.Invoke();
                OnServerInitialize();
            }
        }

        public void Stop()
        {
            UdpProtocol.Stop();
            TcpProtocol.StopListener();
            TcpProtocol.Disconnect();
        }

        public void SendUnreliable(int clientId, Buffer buffer)
        {
            TcpProtocol tcpClient = TcpProtocol.GetClient(clientId);
            UdpProtocol.Send(buffer, new IPEndPoint(IPAddress.Parse(tcpClient.Address), clientUdpPort));
        }

        public void SendReliable(int clientId, Buffer buffer)
        {
            TcpProtocol.SendToClientById(clientId, buffer);
        }

        public void SendMessage(NetworkMessage message, Player player, bool reliable)
        {
            if (reliable)
            {
                SendReliable(player.Id, message.Serialize());
            }
            else
            {
                SendUnreliable(player.Id, message.Serialize());
            }
        }

        public void BroadcastMessage(NetworkMessage message, bool reliable, ushort networkLayer = 0)
        {
            foreach (Player player in playerList)
            {
                if (player.Layer == networkLayer)
                {
                    SendMessage(message, player, reliable);
                }
            }
        }

        public void BroadcastBuffer(Buffer message, bool reliable, ushort networkLayer = 0)
        {
            foreach (Player player in playerList)
            {
                if (player.Layer == networkLayer)
                {
                    if (reliable)
                    {
                        SendReliable(player.Id, message);
                    }
                    else
                    {
                        SendUnreliable(player.Id, message);
                    }
                }
            }
        }

        public async Task AddressableLoadScene(string addressableId, LoadSceneMode loadMode = LoadSceneMode.Single,
            bool activateOnLoad = true, int priority = 100, ushort networkLayer = 0)
        {
            AsyncOperationHandle<SceneInstance> request =
                Addressables.LoadSceneAsync(addressableId, loadMode, activateOnLoad, priority);

            if (!activateOnLoad)
            {
                pendingAddressablesScenes.Add(addressableId, request);
            }

            await request.Task;
            
            loadedAddressablesScenes.Add(addressableId, request);

            Packet packetType;
            if (activateOnLoad)
            {
                packetType = loadMode == LoadSceneMode.Single
                    ? Packet.AddressableLoadSceneSingle
                    : Packet.AddressableLoadSceneAdditive;
            }
            else
            {
                packetType = loadMode == LoadSceneMode.Single
                    ? Packet.AddressableLoadSceneSingleNoActivation
                    : Packet.AddressableLoadSceneAdditiveNoActivation;
            }

            BroadcastMessage(new NetworkMessageAddressableScene(packetType, addressableId), true, networkLayer);
        }

        public void AddressableUnloadScene(string addressableId, ushort networkLayer = 0)
        {
            if (loadedAddressablesScenes.TryGetValue(addressableId, out AsyncOperationHandle<SceneInstance> request))
            {
                Addressables.UnloadSceneAsync(request);
            }
            
            BroadcastMessage(new NetworkMessageAddressableScene(Packet.AddressableUnloadScene, addressableId), true, networkLayer);
        }

        public void AddressableActivateScene(string addressableId, ushort networkLayer = 0)
        {
            if (pendingAddressablesScenes.TryGetValue(addressableId, out AsyncOperationHandle<SceneInstance> request))
            {
                request.Result.ActivateAsync();
            }
            
            BroadcastMessage(new NetworkMessageAddressableScene(Packet.AddressableActivateScene, addressableId), true,
                networkLayer);
        }

        public void LoadScene(string sceneName, LoadSceneMode mode, bool allowSceneActivation, Action onComplete,
            ushort networkLayer = 0)
        {
            AsyncOperation request = SceneManager.LoadSceneAsync(sceneName, mode);
            request.allowSceneActivation = allowSceneActivation;
            if (!allowSceneActivation)
            {
                pendingScenes.Add(sceneName, request);
            }

            request.completed += OnCompleted;

            void OnCompleted(AsyncOperation asyncOperation)
            {
                request.completed -= OnCompleted;
                onComplete.Invoke();

                Packet packetType;
                if (allowSceneActivation)
                {
                    packetType = mode == LoadSceneMode.Single ? Packet.LoadSceneSingle : Packet.LoadSceneAdditive;
                }
                else
                {
                    packetType = mode == LoadSceneMode.Single
                        ? Packet.LoadSceneSingleNoActivation
                        : Packet.LoadSceneAdditiveNoActivation;
                }

                BroadcastMessage(new NetworkMessageScene(packetType, sceneName), true, networkLayer);
            }
        }

        public void UnloadScene(string sceneName, ushort networkLayer = 0)
        {
            SceneManager.UnloadSceneAsync(sceneName);
            
            BroadcastMessage(new NetworkMessageScene(Packet.UnloadScene, sceneName), true, networkLayer);
        }
        
        public void ActivateScene(string sceneName, ushort networkLayer = 0)
        {
            if (pendingScenes.TryGetValue(sceneName, out AsyncOperation asyncOperation))
            {
                asyncOperation.allowSceneActivation = true;
            }
            
            BroadcastMessage(new NetworkMessageScene(Packet.ActivateScene, sceneName), true, networkLayer);
        }

        public T Construct<T>(ushort networkLayer = 0, params object[] initializeArgs)
            where T : INetworkObject, new()
        {
            T networkObject = new T();
            uint networkIdentity = AllocateNetworkIdentity();
            networkObjects.Add(networkIdentity, networkObject);

            NetworkMessageConstruct messageConstruct =
                new NetworkMessageConstruct(networkIdentity, typeof(T).AssemblyQualifiedName, initializeArgs);

            Buffer initializeBuffer = messageConstruct.Serialize();
            initializeBuffer.BeginReading(NetworkMessage.PACKET_OFFSET + NetworkMessage.SIZEOF_PACKET);
            initializeBuffer.ReadUInt32(); //Move seeker to networkIdentity
            initializeBuffer.ReadString(); //Move seeker to AssemblyQualifiedName
            networkObject.Initialize(networkIdentity, initializeBuffer);

            BroadcastMessage(messageConstruct, true, networkLayer);

            return networkObject;
        }

        public async Task<GameObject> AddressableInstantiate(string addressableId,
            InstantiationParameters instantiateParameters, ushort networkLayer = 0, params object[] initializeArgs)
        {
            AsyncOperationHandle<GameObject> request =
                Addressables.InstantiateAsync(addressableId, instantiateParameters);
            await request.Task;

            NetworkObjectBehaviour networkObjectBehaviour = GetOrAddNetworkObjectBehaviour(request.Result);
            Transform transform = request.Result.GetComponent<Transform>();

            BroadcastInstantiate(addressableId, networkLayer,
                transform.position, transform.rotation, instantiateParameters.Parent,
                networkObjectBehaviour, Packet.AddressableInstantiate, initializeArgs);

            return request.Result;
        }

        public void AddressableInstantiate(string addressableId, InstantiationParameters instantiateParameters,
            ushort networkLayer, Action<GameObject> onComplete, params object[] initializeArgs)
        {
            AsyncOperationHandle<GameObject> request =
                Addressables.InstantiateAsync(addressableId, instantiateParameters);
            request.Completed += OnCompleted;

            void OnCompleted(AsyncOperationHandle<GameObject> handler)
            {
                request.Completed -= OnCompleted;
                onComplete?.Invoke(handler.Result);

                NetworkObjectBehaviour networkObjectBehaviour = GetOrAddNetworkObjectBehaviour(request.Result);
                Transform transform = request.Result.GetComponent<Transform>();

                BroadcastInstantiate(addressableId, networkLayer,
                    transform.position, transform.rotation, instantiateParameters.Parent,
                    networkObjectBehaviour, Packet.AddressableInstantiate, initializeArgs);
            }
        }

        public async Task<GameObject> ResourcesInstantiate(string path, InstantiationParameters instantiateParameters,
            ushort networkLayer, params object[] initializeArgs)
        {
            ResourceRequest request = Resources.LoadAsync<GameObject>(path);

            while (!request.isDone)
            {
                await Task.Delay(25);
            }

            GameObject instance = instantiateParameters.Instantiate(request.asset as GameObject);
            NetworkObjectBehaviour networkObjectBehaviour = GetOrAddNetworkObjectBehaviour(instance);
            Transform transform = instance.GetComponent<Transform>();

            BroadcastInstantiate(path, networkLayer,
                transform.position, transform.rotation, instantiateParameters.Parent,
                networkObjectBehaviour, Packet.AddressableInstantiate, initializeArgs);

            return instance;
        }

        public void ResourcesInstantiate(string path, InstantiationParameters instantiateParameters,
            ushort networkLayer, Action<GameObject> onComplete, params object[] initializeArgs)
        {
            ResourceRequest request = Resources.LoadAsync<GameObject>(path);
            request.completed += OnCompleted;

            void OnCompleted(AsyncOperation operation)
            {
                request.completed -= OnCompleted;

                GameObject instance = instantiateParameters.Instantiate(request.asset as GameObject);
                NetworkObjectBehaviour networkObjectBehaviour = GetOrAddNetworkObjectBehaviour(instance);
                Transform transform = instance.GetComponent<Transform>();

                BroadcastInstantiate(path, networkLayer,
                    transform.position, transform.rotation, instantiateParameters.Parent,
                    networkObjectBehaviour, Packet.AddressableInstantiate, initializeArgs);

                onComplete?.Invoke(request.asset as GameObject);
            }
        }

        public void DisposeAndDestroy(uint networkIdentity, ushort networkLayer = 0)
        {
            if (networkObjects.TryGetValue(networkIdentity, out INetworkObject networkObject))
            {
                if (networkObject is MonoBehaviour networkObjectMb)
                {
                    UnityEngine.Object.Destroy(networkObjectMb.gameObject);
                }
                else
                {
                    networkObject.Dispose();
                }

                networkObjects.Remove(networkIdentity);

                BroadcastMessage(new NetworkMessageDisposeAndDestroy(networkIdentity), true, networkLayer);
            }
        }

        public void KickPlayer(Player player)
        {
            TcpProtocol.KickClient(player.Id);
        }

        public void Update()
        {
            UdpProtocol.Update();
            TcpProtocol.UpdateListener();
            TcpProtocol.UpdateClient();
        }

        protected virtual void OnServerInitialize()
        {
        }

        protected virtual void OnMessageReceive(Buffer buffer, Player player)
        {
        }

        protected virtual void OnPlayerConnect(Player player)
        {
        }

        protected virtual void OnPlayerDisconnect(Player player)
        {
        }

        private NetworkObjectBehaviour GetOrAddNetworkObjectBehaviour(GameObject gameObject)
        {
            NetworkObjectBehaviour networkObjectBehaviour = gameObject.GetComponent<NetworkObjectBehaviour>();
            if (networkObjectBehaviour == null)
            {
                networkObjectBehaviour = gameObject.AddComponent<NetworkObjectBehaviour>();
            }

            uint networkIdentity = AllocateNetworkIdentity();
            networkObjects.Add(networkIdentity, networkObjectBehaviour);

            return networkObjectBehaviour;
        }

        private void BroadcastInstantiate(string addressableId, ushort networkLayer,
            Vector3 position, Quaternion rotation, Transform parent,
            INetworkObject networkObject, Packet packet,
            object[] initializeArgs)
        {
            uint networkIdentity = AllocateNetworkIdentity();
            networkObjects.Add(networkIdentity, networkObject);

            uint parentNetworkId = 0;
            if (parent != null)
            {
                NetworkObjectBehaviour parentNetworkObj = parent.GetComponent<NetworkObjectBehaviour>();
                parentNetworkId = parentNetworkObj.NetworkIdentity;
            }

            NetworkMessage networkMessage = null;
            switch (packet)
            {
                case Packet.AddressableInstantiate:
                {
                    networkMessage =
                        new NetworkMessageAddressableInstantiate(networkIdentity, addressableId,
                            position, rotation, parentNetworkId,
                            initializeArgs);
                    break;
                }
                case Packet.ResourcesInstantiate:
                {
                    networkMessage =
                        new NetworkMessageResourceInstantiate(networkIdentity, addressableId,
                            position, rotation, parentNetworkId,
                            initializeArgs);
                    break;
                }
            }

            Buffer initializeBuffer = networkMessage.Serialize();
            initializeBuffer.BeginReading(NetworkMessage.PACKET_OFFSET + NetworkMessage.SIZEOF_PACKET);
            initializeBuffer.ReadUInt32(); //Move seeker to networkIdentity
            initializeBuffer.ReadString(); //Move seeker to AssemblyQualifiedName
            networkObject.Initialize(networkIdentity, initializeBuffer);

            BroadcastMessage(networkMessage, true, networkLayer);
        }

        private void OnTcpClientConnect(TcpProtocol tcpProtocol)
        {
            Player newPlayer = new Player(this,
                new IPEndPoint(IPAddress.Parse(tcpProtocol.Ip), clientUdpPort));

            playerList.Add(newPlayer);
            playerTableById.Add(newPlayer.Id, newPlayer);
            playerTableByEndPoint.Add(tcpProtocol.EndPoint, newPlayer);

            OnPlayerConnected?.Invoke(newPlayer);
            OnPlayerConnect(newPlayer);
        }

        private void OnTcpClientDisconnect(TcpProtocol tcpProtocol)
        {
            Player found = playerList.Find(item => item.TcpProtocol == tcpProtocol);
            if (found != null)
            {
                playerList.Remove(found);
                playerTableById.Remove(found.Id);
                playerTableByEndPoint.Remove(found.TcpProtocol.EndPoint);

                OnPlayerDisconnected?.Invoke(found);
                OnPlayerDisconnect(found);
            }
        }

        private void OnUdpPacketReceived(Buffer buffer, IPEndPoint source)
        {
            if (playerTableByEndPoint.TryGetValue(source, out Player player))
            {
                OnMessageReceived?.Invoke(buffer, player);
                OnMessageReceive(buffer, player);
            }
        }

        private void OnListenerPacketReceived(Buffer buffer, IPEndPoint source)
        {
            Packet packetType = buffer.PeekPacket();

            if (!playerTableByEndPoint.TryGetValue(source, out Player player))
            {
                Debug.LogError("Player not found");
                return;
            }

            switch (packetType)
            {
                case Packet.PlayerName:
                {
                    player.Name = buffer.Deserialize<NetworkMessagePlayerName>().Name;
                    break;
                }
            }

            OnMessageReceived?.Invoke(buffer, player);
            OnMessageReceive(buffer, player);
        }

        private uint AllocateNetworkIdentity()
        {
            lock (lastAllocatedNetworkIdentityObj)
            {
                uint result;
                uint attempts = uint.MaxValue;
                do
                {
                    result = lastAllocatedNetworkIdentity + 1;
                    if (result >= uint.MaxValue)
                    {
                        result = 0;
                    }

                    attempts--;
                    if (attempts <= 0)
                    {
                        throw new Exception(
                            "[Server:AllocateNetworkIdentity] -> There is no network identity to available");
                    }
                } while (networkObjects.ContainsKey(result));

                lastAllocatedNetworkIdentity = result;
                return result;
            }
        }
    }
}