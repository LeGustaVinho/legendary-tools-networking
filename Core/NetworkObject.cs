using System;
using UnityEngine;

namespace LegendaryTools.Networking
{
    public interface INetworkObject : IDisposable
    {
        uint NetworkIdentity { get; }
        bool IsInitialized { get; }
        void Initialize(uint networkIdentity, Buffer buffer);
        void OnInitialize(Buffer buffer);
    }

    [Serializable]
    public abstract class NetworkObject : INetworkObject
    {
        public uint NetworkIdentity { private set; get; }

        public bool IsInitialized { private set; get; }

        public void Initialize(uint networkIdentity, Buffer buffer)
        {
            if (!IsInitialized)
            {
                NetworkIdentity = networkIdentity;
                OnInitialize(buffer);
                IsInitialized = true;
            }
        }

        public abstract void OnInitialize(Buffer buffer);
        
        public virtual void Dispose()
        {
            IsInitialized = false;
        }
    }

    public abstract class NetworkObjectBehaviour : MonoBehaviour, INetworkObject
    {
        public uint NetworkIdentity { private set; get; }

        public bool IsInitialized { private set; get; }

        public void Initialize(uint networkIdentity, Buffer buffer)
        {
            if (!IsInitialized)
            {
                NetworkIdentity = networkIdentity;
                OnInitialize(buffer);
                IsInitialized = true;
            }
        }

        public abstract void OnInitialize(Buffer buffer);

        protected virtual void OnDestroy()
        {
            Dispose();
        }
        
        public virtual void Dispose()
        {
            IsInitialized = false;
        }
    }
}