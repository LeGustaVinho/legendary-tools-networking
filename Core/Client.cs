using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace LegendaryTools.Networking
{
    public class Client
    {
        public event Action OnConnectedToServer;
        public event Action OnDisconnectedFromServer;
        public event Action OnFailedToConnect;
        public event Action<Buffer> OnMessageReceived;
        public event Action<string, LoadSceneMode, float> OnLoadingScene;

        public Player LocalPlayer { private set; get; }

        private readonly UdpProtocol UdpProtocol = new UdpProtocol("Client");
        protected internal readonly TcpProtocol TcpProtocol = new TcpProtocol();

        protected byte lastAllocatedRequestId;
        protected readonly Dictionary<byte, Action<Buffer>> pendingResponse = new Dictionary<byte, Action<Buffer>>();
        protected readonly Dictionary<uint, INetworkObject> networkObjects = new Dictionary<uint, INetworkObject>();

        private IPEndPoint serverUdpAddress;
        private IPEndPoint serverTcpAddress;

        private readonly Dictionary<string, AsyncOperation> pendingSceneActivation = new Dictionary<string, AsyncOperation>();
        private readonly Dictionary<string, AsyncOperationHandle<SceneInstance>> pendingAddressableSceneActivation 
            = new Dictionary<string, AsyncOperationHandle<SceneInstance>>();
        private readonly Dictionary<string, AsyncOperationHandle<SceneInstance>> loadedAddressableScene
            = new Dictionary<string, AsyncOperationHandle<SceneInstance>>();

        public Client()
        {
            UdpProtocol.OnPacketReceived += OnUdpPacketReceived;
            TcpProtocol.OnClientPacketReceived += OnClientPacketReceived;
            TcpProtocol.OnTcpSocketClose += OnTcpSocketClose;
        }

        ~Client()
        {
            UdpProtocol.OnPacketReceived -= OnUdpPacketReceived;
            TcpProtocol.OnClientPacketReceived -= OnClientPacketReceived;
            TcpProtocol.OnTcpSocketClose -= OnTcpSocketClose;
        }

        public void Connect(IPEndPoint serverUdpAddress, IPEndPoint serverTcpAddress, int clientUdpListenerPort = 25001)
        {
            this.serverUdpAddress = serverUdpAddress;
            this.serverTcpAddress = serverTcpAddress;

            bool udpConnected = UdpProtocol.Start(clientUdpListenerPort);
            bool tcpConnected = TcpProtocol.Connect(serverTcpAddress);

            if (udpConnected && tcpConnected)
            {
                LocalPlayer = new Player(this);
                OnConnectedToServer?.Invoke();
                OnConnectToServer();
            }
            else
            {
                OnFailedToConnect?.Invoke();
                OnFailToConnect();
            }
        }

        public void Disconnect()
        {
            UdpProtocol.Stop();
            TcpProtocol.Disconnect();
            LocalPlayer = null;
        }

        public void SendUnreliable(Buffer buffer)
        {
            UdpProtocol.Send(buffer, serverUdpAddress);
        }

        public void SendReliable(Buffer buffer)
        {
            TcpProtocol.SendTcpPacket(buffer);
        }

        public void SendMessage(NetworkMessage message, bool reliable)
        {
            if (reliable)
            {
                SendReliable(message.Serialize());
            }
            else
            {
                SendUnreliable(message.Serialize());
            }
        }

        public void SendRequest(NetworkMessageRequest message, Action<Buffer> responseCallback)
        {
            lock (pendingResponse)
            {
                message.RequestId = AllocateRequestID();
                pendingResponse.Add(message.RequestId, responseCallback);
            }

            SendMessage(message, true);
        }

        public void SendKeepAlive()
        {
            Buffer buffer = Buffer.CreatePackage(Packet.KeepAlive, out BinaryWriter writer);
            buffer.EndPacket();
            SendReliable(buffer);
        }

        public void Update()
        {
            UdpProtocol.Update();
            TcpProtocol.UpdateClient();
        }

        protected virtual void OnConnectToServer()
        {
        }

        protected virtual void OnDisconnectToServer()
        {
        }

        protected virtual void OnFailToConnect()
        {
        }

        protected virtual void OnMessageReceive(Buffer buffer)
        {
        }

        protected virtual void OnLoadingScenes(string sceneName, LoadSceneMode mode, float progress)
        {
        }

        private void OnUdpPacketReceived(Buffer buffer, IPEndPoint source)
        {
            OnMessageReceived?.Invoke(buffer);
            OnMessageReceive(buffer);
        }

        private void OnClientPacketReceived(Buffer buffer, IPEndPoint source)
        {
            Packet packetType = buffer.PeekPacket();

            switch (packetType)
            {
                case Packet.Disconnect:
                {
                    OnDisconnectedFromServer?.Invoke();
                    buffer.Recycle();
                    return;
                }
                case Packet.ResponseMessage:
                {
                    byte response = buffer.PeekByte(NetworkMessageResponse.RESPONSE_ID_OFFSET);
                    if (pendingResponse.TryGetValue(response, out Action<Buffer> responseCallback))
                    {
                        pendingResponse.Remove(response);
                        responseCallback.Invoke(buffer);
                    }
                    buffer.Recycle();
                    return;
                }
                case Packet.PlayerLayer:
                {
                    LocalPlayer.Layer = buffer.Deserialize<NetworkMessagePlayerLayer>().Layer;
                    buffer.Recycle();
                    return;
                }
                case Packet.PlayerName:
                {
                    LocalPlayer.Name = buffer.Deserialize<NetworkMessagePlayerName>().Name;
                    buffer.Recycle();
                    return;
                }
                case Packet.AddressableInstantiate:
                {
                    AddressableInstantiate(buffer);
                    return;
                }
                case Packet.ResourcesInstantiate:
                {
                    ResourcesInstantiate(buffer);
                    return;
                }
                case Packet.Construct:
                {
                    ConstructInstance(buffer);
                    return;
                }
                case Packet.DisposeAndDestroy:
                {
                    DisposeAndDestroy(buffer);
                    return;
                }
                case Packet.SyncVar:
                {
                    return;
                }
                case Packet.RPC:
                {
                    return;
                }
                case Packet.LoadSceneSingle:
                case Packet.LoadSceneAdditive:
                case Packet.LoadSceneSingleNoActivation:
                case Packet.LoadSceneAdditiveNoActivation:
                {
                    LoadScene(buffer, packetType);
                    return;
                }
                case Packet.UnloadScene:
                {
                    UnloadScene(buffer);
                    return;
                }
                case Packet.ActivateScene:
                {
                    ActivateScene(buffer);
                    return;
                }
                
                case Packet.AddressableLoadSceneSingle:
                case Packet.AddressableLoadSceneAdditive:
                case Packet.AddressableLoadSceneSingleNoActivation:
                case Packet.AddressableLoadSceneAdditiveNoActivation:
                {
                    AddressableLoadScene(buffer, packetType);
                    return;
                }
                case Packet.AddressableUnloadScene:
                {
                    AddressableUnloadScene(buffer);
                    return;
                }
                case Packet.AddressableActivateScene:
                {
                    AddressableActivateScene(buffer);
                    return;
                }
            }

            OnMessageReceived?.Invoke(buffer);
            OnMessageReceive(buffer);
            buffer.Recycle();
        }

        private void ConstructInstance(Buffer buffer)
        {
            NetworkMessageConstruct networkMessageConstruct = buffer.Deserialize<NetworkMessageConstruct>(false);
            INetworkObject networkObject = networkMessageConstruct.CreateInstance();

            if (networkObject != null)
            {
                networkObjects.Add(networkObject.NetworkIdentity, networkObject);
                networkObject.Initialize(networkMessageConstruct.NetworkIdentity, buffer);
            }

            buffer.Recycle();
        }

        private void DisposeAndDestroy(Buffer buffer)
        {
            NetworkMessageDisposeAndDestroy messageDisposeAndDestroy = buffer.Deserialize<NetworkMessageDisposeAndDestroy>();
            
            if (networkObjects.TryGetValue(messageDisposeAndDestroy.NetworkIdentity, out INetworkObject networkObject))
            {
                if (networkObject is MonoBehaviour networkObjectMb)
                {
                    UnityEngine.Object.Destroy(networkObjectMb.gameObject);
                }
                else
                {
                    networkObject.Dispose();
                }

                networkObjects.Remove(messageDisposeAndDestroy.NetworkIdentity);
            }

            buffer.Recycle();
        }

        private void ActivateScene(Buffer buffer)
        {
            NetworkMessageScene scene = buffer.Deserialize<NetworkMessageScene>();
            if (pendingSceneActivation.TryGetValue(scene.SceneName, out AsyncOperation asyncOperation))
            {
                asyncOperation.allowSceneActivation = true;
            }

            pendingSceneActivation.Remove(scene.SceneName);
            buffer.Recycle();
        }

        private void LoadScene(Buffer buffer, Packet packetType)
        {
            bool allowSceneActivation =
                packetType == Packet.LoadSceneSingle || packetType == Packet.LoadSceneAdditive;
            LoadSceneMode sceneMode =
                packetType == Packet.LoadSceneSingle || packetType == Packet.LoadSceneSingleNoActivation
                    ? LoadSceneMode.Single
                    : LoadSceneMode.Additive;

            NetworkMessageScene scene = buffer.Deserialize<NetworkMessageScene>();
            AsyncOperation asyncOperation = SceneManager.LoadSceneAsync(scene.SceneName, sceneMode);
            asyncOperation.allowSceneActivation = allowSceneActivation;
            
            if (!allowSceneActivation)
            {
                pendingSceneActivation.AddOrUpdate(scene.SceneName, asyncOperation);
            }

            asyncOperation.completed += OnComplete;

            void OnComplete(AsyncOperation obj)
            {
                asyncOperation.completed -= OnComplete;
                OnLoadingScene?.Invoke(scene.SceneName, sceneMode, asyncOperation.progress);
                OnLoadingScenes(scene.SceneName, sceneMode, asyncOperation.progress);
            }
            
            buffer.Recycle();
        }

        private void UnloadScene(Buffer buffer)
        {
            NetworkMessageScene scene = buffer.Deserialize<NetworkMessageScene>();
            SceneManager.UnloadSceneAsync(scene.SceneName);
            buffer.Recycle();
        }
        
        private void AddressableActivateScene(Buffer buffer)
        {
            NetworkMessageAddressableScene scene = buffer.Deserialize<NetworkMessageAddressableScene>();
            if (pendingAddressableSceneActivation.TryGetValue(scene.AddressableId, out AsyncOperationHandle<SceneInstance> asyncOperation))
            {
                asyncOperation.Result.ActivateAsync();
            }

            pendingAddressableSceneActivation.Remove(scene.AddressableId);
            buffer.Recycle();
        }

        private void AddressableLoadScene(Buffer buffer, Packet packetType)
        {
            bool allowSceneActivation =
                packetType == Packet.AddressableLoadSceneSingle || packetType == Packet.AddressableLoadSceneAdditive;
            LoadSceneMode sceneMode =
                packetType == Packet.AddressableLoadSceneSingle || packetType == Packet.AddressableLoadSceneSingleNoActivation
                    ? LoadSceneMode.Single
                    : LoadSceneMode.Additive;

            NetworkMessageAddressableScene scene = buffer.Deserialize<NetworkMessageAddressableScene>();
            AsyncOperationHandle<SceneInstance> request = Addressables.LoadSceneAsync(scene.AddressableId, sceneMode, allowSceneActivation);
            request.Completed += OnCompleted;
            
            if (!allowSceneActivation)
            {
                pendingAddressableSceneActivation.Add(scene.AddressableId, request);
            }
            
            void OnCompleted(AsyncOperationHandle<SceneInstance> asyncOperationHandle)
            {
                request.Completed -= OnCompleted;
                
                loadedAddressableScene.Add(scene.AddressableId, request);
                
                OnLoadingScene?.Invoke(asyncOperationHandle.Result.Scene.name, sceneMode, asyncOperationHandle.PercentComplete);
                OnLoadingScenes(asyncOperationHandle.Result.Scene.name, sceneMode, asyncOperationHandle.PercentComplete);
            }

            buffer.Recycle();
        }

        private void AddressableUnloadScene(Buffer buffer)
        {
            NetworkMessageAddressableScene scene = buffer.Deserialize<NetworkMessageAddressableScene>();

            if (loadedAddressableScene.TryGetValue(scene.AddressableId, out AsyncOperationHandle<SceneInstance> asyncOperationHandle))
            {
                Addressables.UnloadSceneAsync(asyncOperationHandle);
            }

            buffer.Recycle();
        }
        
        private void ResourcesInstantiate(Buffer buffer)
        {
            NetworkMessageResourceInstantiate resourcesInstantiateMsg = buffer.Deserialize<NetworkMessageResourceInstantiate>();
            networkObjects.TryGetValue(resourcesInstantiateMsg.Parent,
                out INetworkObject parentNetworkObject);

            ResourceRequest request = Resources.LoadAsync<GameObject>(resourcesInstantiateMsg.Path);

            request.completed += OnCompleted;

            void OnCompleted(AsyncOperation operation)
            {
                request.completed -= OnCompleted;

                InstantiationParameters instantiateParameters = new InstantiationParameters(resourcesInstantiateMsg.Position,
                    resourcesInstantiateMsg.Rotation,
                    (parentNetworkObject as NetworkObjectBehaviour)?.transform);

                GameObject instance = instantiateParameters.Instantiate(request.asset as GameObject);
                NetworkObjectBehaviour networkObjectBehaviour = instance.GetComponent<NetworkObjectBehaviour>();

                if (networkObjectBehaviour != null)
                {
                    networkObjects.Add(networkObjectBehaviour.NetworkIdentity, networkObjectBehaviour);
                    networkObjectBehaviour.Initialize(networkObjectBehaviour.NetworkIdentity, buffer);
                }

                buffer.Recycle();
            }
        }

        private void AddressableInstantiate(Buffer buffer)
        {
            NetworkMessageAddressableInstantiate addressableInstantiateMsg =
                buffer.Deserialize<NetworkMessageAddressableInstantiate>();

            networkObjects.TryGetValue(addressableInstantiateMsg.Parent,
                out INetworkObject parentNetworkObject);

            AsyncOperationHandle<GameObject> request = Addressables.InstantiateAsync(addressableInstantiateMsg.AddressableId,
                new InstantiationParameters(addressableInstantiateMsg.Position,
                    addressableInstantiateMsg.Rotation,
                    (parentNetworkObject as NetworkObjectBehaviour)?.transform));

            request.Completed += OnCompleted;

            void OnCompleted(AsyncOperationHandle<GameObject> handler)
            {
                request.Completed -= OnCompleted;
                NetworkObjectBehaviour networkObjectBehaviour = handler.Result.GetComponent<NetworkObjectBehaviour>();

                if (networkObjectBehaviour != null)
                {
                    networkObjects.Add(networkObjectBehaviour.NetworkIdentity, networkObjectBehaviour);
                    networkObjectBehaviour.Initialize(networkObjectBehaviour.NetworkIdentity, buffer);
                }
                
                buffer.Recycle();
            }
        }

        private void OnTcpSocketClose()
        {
            LocalPlayer = null;
            OnDisconnectedFromServer?.Invoke();
            OnDisconnectToServer();
        }

        private byte AllocateRequestID()
        {
            byte result = lastAllocatedRequestId++;
            if (lastAllocatedRequestId >= byte.MaxValue)
            {
                lastAllocatedRequestId = 0;
            }

            return result;
        }
    }
}