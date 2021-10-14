using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace LegendaryTools.Networking
{
    public delegate void OnUdpPacketReceivedEventHandler(Buffer buffer, IPEndPoint source);

    /// <summary>
    /// UDP class makes it possible to broadcast messages to players on the same network prior to establishing a connection.
    /// </summary>
    public class UdpProtocol
    {
        public const int BUFFER_MAX_SIZE = 8192;

        /// <summary>
        /// If 'true', network system will use multicasting with new UDP sockets. If 'false', network system will use broadcasting instead.
        /// Multicasting is the suggested way to go as it supports multiple network interfaces properly.
        /// It's important to set this prior to calling Start or the change won't have any effect.
        /// </summary>
        public static bool useMulticasting = true;

        /// <summary>
        /// When you have multiple network interfaces, it's often important to be able to specify
        /// which interface will actually be used to send UDP messages. By default this will be set
        /// to IPAddress.Any, but you can change it to be something else if you desire.
        /// It's important to set this prior to calling StartUDP or the change won't have any effect.
        /// </summary>
        public static IPAddress defaultNetworkInterface = null;

        protected static readonly object lockObj = new int();

        // Default end point -- mEndPoint is reset to this value after every receive operation.
        private static EndPoint defaultEndPoint;

        // End point of where the data is coming from
        private EndPoint endPoint;

        // Incoming message queue
        protected Queue<Datagram> inQueue = new Queue<Datagram>();
        private bool multicast = true;
        protected Queue<Datagram> outQueue = new Queue<Datagram>();

        // Port used to listen and socket used to send and receive
        private int Port = -1;

        private Socket socket;

        private Socket Socket
        {
            get => socket;
            set => socket = value;
        }

        // Buffer used for receiving incoming data
        private byte[] temp = new byte[BUFFER_MAX_SIZE];
        private Thread thread;

#if !UNITY_WEBPLAYER
        // Cached broadcast end-point
        private static IPAddress multicastIP = IPAddress.Parse("224.168.100.17");
        private IPEndPoint multicastEndPoint = new IPEndPoint(multicastIP, 0);
        private IPEndPoint broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, 0);
#endif
        
        /// <summary>
        /// Whether we can send or receive through the UDP socket.
        /// </summary>
        public bool isActive => Port != -1;

        /// <summary>
        /// Port used for listening.
        /// </summary>

        public int listeningPort => Port > 0 ? Port : 0;

        public event OnUdpPacketReceivedEventHandler OnPacketReceived;

        public string Name;

        public UdpProtocol()
        {
            
        }
        
        public UdpProtocol(string name)
        {
            Name = name;
        }
        
        /// <summary>
        /// Start UDP, but don't bind it to a specific port. This means we will be able to send, but not receive.
        /// </summary>
        public bool Start()
        {
            return Start(0);
        }

        /// <summary>
        /// Start listening for incoming messages on the specified port.
        /// </summary>
        public bool Start(int port)
        {
            Stop();

            Port = port;
            Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                MulticastLoopback = true
            };

            // Web player doesn't seem to support broadcasts
            multicast = useMulticasting;

            try
            {
                if (useMulticasting)
                {
                    ListLessGarb<IPAddress> ips = NetworkUtility.localAddresses;

                    foreach (IPAddress ip in ips)
                    {
                        MulticastOption opt = new MulticastOption(multicastIP, ip);
                        Socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, opt);
                    }
                }
                else
                {
                    Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            // Port zero means we will be able to send, but not receive
            if (Port == 0)
            {
#if DEBUG
                Debug.Log("[UdpProtocol:Start(" + port +
                          ") - Port zero means we will be able to send, but not receive");
#endif
                return true;
            }

            try
            {
                // Use the default network interface if one wasn't explicitly chosen
#if (UNITY_IPHONE && !UNITY_EDITOR) //|| UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
			IPAddress networkInterface = useMulticasting ? multicastIP : (defaultNetworkInterface ?? IPAddress.Any);
#else
                IPAddress networkInterface = defaultNetworkInterface ?? IPAddress.Any;
#endif
                endPoint = new IPEndPoint(networkInterface, 0);
                defaultEndPoint = new IPEndPoint(networkInterface, 0);

                // Bind the socket to the specific network interface and start listening for incoming packets
                Socket.Bind(new IPEndPoint(networkInterface, Port));
                Socket.BeginReceiveFrom(temp, 0, temp.Length, SocketFlags.None, ref endPoint, OnReceive, null);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Stop();
                return false;
            }

#if MULTI_THREADED
            thread = new Thread(ThreadProcessPackets);
            thread.Start();
#endif

#if DEBUG
            Debug.Log("[UdpProtocol:Start(" + port + ") - Success ! useMulticasting? " + useMulticasting +
                      " EndPoint: " + endPoint);
#endif
            return true;
        }

        /// <summary>
        /// Stop listening for incoming packets.
        /// </summary>
        public void Stop()
        {
            Port = -1;

            if (Socket != null)
            {
                Socket.Close();
                Socket = null;

#if DEBUG
                Debug.Log("[UdpProtocol:Stop() - Stopped");
#endif
            }
            else
            {
#if DEBUG
                Debug.LogWarning("[UdpProtocol:Stop() - mSocket is null");
#endif
            }

#if MULTI_THREADED
            // Stop the worker thread
            if (thread != null)
            {
                thread.Abort();
                thread = null;
            }
#endif
            Buffer.Recycle(inQueue);
            Buffer.Recycle(outQueue);
        }

        /// <summary>
        /// Receive incoming data.
        /// </summary>
        private void OnReceive(IAsyncResult result)
        {
            if (!isActive)
            {
                return;
            }
            int bytes = 0;

            try
            {
                bytes = Socket.EndReceiveFrom(result, ref endPoint);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Error(new IPEndPoint(NetworkUtility.localAddress, 0), ex.Message);
            }

            if (bytes > 4)
            {
                // This datagram is now ready to be processed
                Buffer buffer = Buffer.Create();
                buffer.BeginWriting(false).Write(temp, 0, bytes);
                buffer.BeginReading(4); //The first 4 bytes is the buffer size, so move the cursor 4 bytes forward to make it easier to read and go straight to the data

                // The 'endPoint', gets reassigned rather than updated.
                Datagram dg = new Datagram {buffer = buffer, ip = (IPEndPoint) endPoint};
                
                lock (inQueue)
                {
                    inQueue.Enqueue(dg);
                }
#if DEBUG
                Debug.Log("[UdpProtocol:OnReceive() - Datagram Enqueue. Queue In count: " + inQueue.Count);
#endif
            }

            // Queue up the next receive operation
            if (Socket != null)
            {
                endPoint = defaultEndPoint;
                Socket.BeginReceiveFrom(temp, 0, temp.Length, SocketFlags.None, ref endPoint, OnReceive, null);
#if DEBUG
                Debug.Log("[UdpProtocol:OnReceive() - Begin receive process from endPoint again. Queue In count: " +
                          inQueue.Count);
#endif
            }
        }

        private void ThreadProcessPackets()
        {
#if MULTI_THREADED
            for (;;)
#endif
            {
                Buffer mReceiveBuffer;
                IPEndPoint mReceiveSource;
                bool received = false;

                lock (lockObj)
                {
                    try
                    {
                        if (ReceivePacket(out mReceiveBuffer, out mReceiveSource))
                        {
                            received = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }

#if MULTI_THREADED
                if (!received)
                {
                    Thread.Sleep(1);
                }
#endif
            }
        }

        /// <summary>
        /// Call this function when you've disabled multi-threading.
        /// </summary>
        public void Update()
        {
            if (thread == null && Socket != null)
            {
                ThreadProcessPackets();
            }
        }

        /// <summary>
        /// Extract the first incoming packet.
        /// </summary>
        public bool ReceivePacket(out Buffer buffer, out IPEndPoint source)
        {
            if (Port == 0)
            {
                Stop();
                throw new InvalidOperationException(
                    "You must specify a non-zero port to UdpProtocol.Start() before you can receive data.");
            }
            if (inQueue.Count != 0)
            {
                lock (inQueue)
                {
                    Datagram dg = inQueue.Dequeue();
                    buffer = dg.buffer;
                    source = dg.ip;
#if DEBUG
                    Debug.Log("[UdpProtocol:ReceivePacket(" + buffer.Size + ", " + source +
                              ") - Receiving packet ....");
#endif
                    OnPacketReceived?.Invoke(buffer, source);

                    return true;
                }
            }

            buffer = null;
            source = null;
            return false;
        }

        /// <summary>
        /// Send an empty packet to the target destination.
        /// Can be used for NAT punch-through, or just to keep a UDP connection alive.
        /// Empty packets are simply ignored.
        /// </summary>
        public void SendEmptyPacket(IPEndPoint ip)
        {
            Buffer buffer = Buffer.Create(false);
            buffer.BeginPacket(Packet.Empty);
            buffer.EndPacket();
            Send(buffer, ip);
        }

        /// <summary>
        /// Send the specified buffer to the entire LAN.
        /// </summary>
        public void Broadcast(Buffer buffer, int port)
        {
            if (buffer != null)
            {
                buffer.MarkAsUsed();
                IPEndPoint endPoint = multicast ? multicastEndPoint : broadcastEndPoint;
                endPoint.Port = port;

                try
                {
                    Socket.SendTo(buffer.DataBuffer, buffer.Position, buffer.Size, SocketFlags.None, endPoint);
#if DEBUG
                    Debug.Log("[UdpProtocol:Broadcast(" + buffer.Size + ", " + port + ") - Sended. Position: " +
                              buffer.Position + " EndPoint: " + endPoint);
#endif
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Error(null, ex.Message);
                }

                buffer.Recycle();
            }
        }

        /// <summary>
        /// Send the specified datagram.
        /// </summary>
        public void Send(Buffer buffer, IPEndPoint ip)
        {
            if (ip.Address.Equals(IPAddress.Broadcast))
            {
                Broadcast(buffer, ip.Port);
                return;
            }

            buffer.MarkAsUsed();

            if (Socket != null)
            {
                buffer.BeginReading();

                lock (outQueue)
                {
                    Datagram dg = new Datagram {buffer = buffer, ip = ip};
                    outQueue.Enqueue(dg);

                    if (outQueue.Count == 1)
                    {
                        // If it's the first datagram, begin the sending process
                        Socket.BeginSendTo(buffer.DataBuffer, buffer.Position, buffer.Size,
                            SocketFlags.None, ip, OnSend, null);
#if DEBUG
                        Debug.Log("[UdpProtocol:Send(" + buffer.Size + ", " + ip + ") - Sent. Position: " +
                                  buffer.Position);
#endif
                    }
                }
            }
            else
            {
                buffer.Recycle();
                throw new InvalidOperationException("The socket is null. Did you forget to call UdpProtocol.Start()?");
            }
        }

        /// <summary>
        /// Send completion callback. Recycles the datagram.
        /// </summary>
        private void OnSend(IAsyncResult result)
        {
            if (!isActive)
            {
                return;
            }
            int bytes = 0;

            try
            {
                bytes = Socket.EndSendTo(result);
            }
            catch (Exception ex)
            {
                bytes = 1;
                Debug.LogException(ex);
            }

            lock (outQueue)
            {
                outQueue.Dequeue().buffer.Recycle(); //remove from queue and recycle buffer

                if (bytes > 0 && Socket != null && outQueue.Count != 0)
                {
                    // If there is another packet to send out, let's send it
                    Datagram dg = outQueue.Peek();
                    Socket.BeginSendTo(dg.buffer.DataBuffer, dg.buffer.Position, dg.buffer.Size,
                        SocketFlags.None, dg.ip, OnSend, null);
#if DEBUG
                    Debug.Log("[UdpProtocol:OnSend() - EndSend. Begin send again. Position: " + dg.buffer.Position);
#endif
                }
                else
                {
#if DEBUG
                    Debug.Log("[UdpProtocol:OnSend() - EndSend.");
#endif
                }
            }
        }

        /// <summary>
        /// Add an error packet to the incoming queue.
        /// </summary>
        public void Error(IPEndPoint ip, string error)
        {
            Buffer buffer = Buffer.Create();
            buffer.BeginPacket(Packet.Error).Write(error);
            buffer.EndTcpPacketWithOffset(4);

            Datagram dg = new Datagram {buffer = buffer, ip = ip};
            lock (inQueue)
            {
                inQueue.Enqueue(dg);
            }
        }
    }
}