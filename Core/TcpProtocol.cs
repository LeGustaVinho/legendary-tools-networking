#define DEBUG

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace LegendaryTools.Networking
{
    /// <summary>
    /// Common network communication-based logic: sending and receiving of data via TCP.
    /// </summary>
    public delegate void OnTcpPacketReceivedEventHandler(Buffer buffer, IPEndPoint source);
    public delegate void OnTcpClientChangeEventHandler(TcpProtocol tcpProtocol);

    public class TcpProtocol
    {
        #region TcpClient Vars

        /// <summary>
        /// Protocol version.
        /// </summary>
        public const int VERSION = 1;
        public const int BUFFER_MAX_SIZE = 8192;

        protected object clientLockObj = new int();
        protected int connectionCounter;

        /// <summary>
        /// All players have a unique identifier given by the server.
        /// </summary>
        public int Id = 1;

        public enum ConnectionStatus
        {
            NotConnected,
            Connecting,
            Verifying,
            Connected,
        }

        /// <summary>
        /// Current connection stage.
        /// </summary>
        public ConnectionStatus Status = ConnectionStatus.NotConnected;

        /// <summary>
        /// IP end point of whomever we're connected to.
        /// </summary>
        public IPEndPoint EndPoint;

        /// <summary>
        /// Timestamp of when we received the last message.
        /// </summary>
        public long LastReceivedTime;

        /// <summary>
        /// How long to allow this player to go without packets before disconnecting them.
        /// This value is in milliseconds, so 1000 means 1 second.
        /// </summary>
#if UNITY_EDITOR
        public long TimeoutTime = 60000;
#else
	public long TimeoutTime = 20000;
#endif

        // Incoming and outgoing queues
        private readonly Queue<Buffer> inQueue = new Queue<Buffer>();
        private readonly Queue<Buffer> outQueue = new Queue<Buffer>();

        // Buffer used for receiving incoming data
        private readonly byte[] temp = new byte[BUFFER_MAX_SIZE];

        // Current incoming buffer
        private Buffer receiveBuffer;
        private int expected;
        private int offset;
        private Socket socket;
        private bool noDelay;
        private IPEndPoint fallback;
        private readonly ListLessGarb<Socket> connectingList = new ListLessGarb<Socket>();
        private Thread clientThread;

        public event OnTcpPacketReceivedEventHandler OnClientPacketReceived;

        /// <summary>
        /// Whether the connection is currently active.
        /// </summary>

        public bool IsConnected => Status == ConnectionStatus.Connected;

        /// <summary>
        /// Whether we are currently trying to establish a new connection.
        /// </summary>

        public bool IsTryingToConnect => connectingList.size != 0;

        /// <summary>
        /// Enable or disable the Nagle's buffering algorithm (aka NO_DELAY flag).
        /// Enabling this flag will improve latency at the cost of increased bandwidth.
        /// http://en.wikipedia.org/wiki/Nagle's_algorithm
        /// </summary>
        public bool NoDelay
        {
            get { return noDelay; }
            set
            {
                if (noDelay != value)
                {
                    noDelay = value;
#if !UNITY_WINRT
                    socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, noDelay);
#endif
                }
            }
        }

        /// <summary>
        /// Connected target's address.
        /// </summary>

        public string Address => EndPoint != null ? EndPoint.ToString() : "0.0.0.0:0";
        public string Ip => EndPoint != null ? EndPoint.Address.ToString() : "0.0.0.0";
        public int Port => EndPoint?.Port ?? 0;

        #endregion

        #region TcpListener Vars

        protected readonly object listenerLockObj = new int();

        /// <summary>
        /// List of players in a consecutive order for each looping.
        /// </summary>
        private readonly ListLessGarb<TcpProtocol> clients = new ListLessGarb<TcpProtocol>();

        /// <summary>
        /// Dictionary list of players for easy access by ID.
        /// </summary>
        private readonly Dictionary<int, TcpProtocol> clientsDictionary = new Dictionary<int, TcpProtocol>();

        private TcpListener listener;
        private Thread listenerThread;
        private int listenerPort;
        private long time;

        public event OnTcpPacketReceivedEventHandler OnListenerPacketReceived;
        public event OnTcpClientChangeEventHandler OnTcpClientConnect;
        public event OnTcpClientChangeEventHandler OnTcpClientDisconnect;

        /// <summary>
        /// Whether the server is currently actively serving players.
        /// </summary>

        public bool IsActive => listenerThread != null;

        /// <summary>
        /// Whether the server is listening for incoming connections.
        /// </summary>

        public bool IsListening => listener != null;

        /// <summary>
        /// Port used for listening to incoming connections. Set when the server is started.
        /// </summary>

        public int ListenerPort => listener != null ? listenerPort : 0;

        #endregion

        #region TcpClient Methods

        /// <summary>
        /// Try to establish a connection with the specified address.
        /// </summary>
        public bool Connect(IPEndPoint externalIP)
        {
            return Connect(externalIP, null);
        }

        /// <summary>
        /// Try to establish a connection with the specified remote destination.
        /// </summary>
        public bool Connect(IPEndPoint externalIP, IPEndPoint internalIP)
        {
            Disconnect();

            Buffer.Recycle(inQueue);
            Buffer.Recycle(outQueue);

            // Some routers, like Asus RT-N66U don't support NAT Loopback, and connecting to an external IP
            // will connect to the router instead. So if it's a local IP, connect to it first.
            if (internalIP != null && NetworkUtility.GetSubnet(NetworkUtility.localAddress) ==
                NetworkUtility.GetSubnet(internalIP.Address))
            {
                EndPoint = internalIP;
                fallback = externalIP;

#if DEBUG
                Debug.LogWarning("[Client][TcpProtocol:Connect(" + externalIP + "," + internalIP +
                                 ") -> Dont support loopback.");
#endif
            }
            else
            {
                EndPoint = externalIP;
                fallback = internalIP;

#if DEBUG
                Debug.Log("[Client][TcpProtocol:Connect(" + externalIP + "," + internalIP + ") -> Support loopback.");
#endif
            }

            return ConnectToTcpEndPoint();
        }

        /// <summary>
        /// Try to establish a connection with the current tcpEndPoint.
        /// </summary>
        private bool ConnectToTcpEndPoint()
        {
            if (EndPoint != null)
            {
                Status = ConnectionStatus.Connecting;

                try
                {
                    lock (connectingList)
                    {
                        socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        connectingList.Add(socket);
                    }
#if DEBUG
                    Debug.Log("[Client][TcpProtocol:ConnectToTcpEndPoint() -> Connecting to endpoint.");
#endif
                    IAsyncResult result = socket.BeginConnect(EndPoint, OnConnectResult, socket);
                    Thread th = new Thread(CancelConnect);
                    th.Start(result);

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Error(ex.Message);
                }
            }
            else
            {
                Debug.LogError(
                    "[Client][TcpProtocol:ConnectToTcpEndPoint()] -> Unable to resolve the specified address.");
                Error("Unable to resolve the specified address");
            }

            return false;
        }

        /// <summary>
        /// Try to establish a connection with the fallback end point.
        /// </summary>
        private bool ConnectToFallback()
        {
            EndPoint = fallback;
            fallback = null;

            bool connectResult = ConnectToTcpEndPoint();

#if DEBUG
            Debug.Log("[Client][TcpProtocol:ConnectToFallback()] -> Fallback result: " + connectResult);
#endif

            return EndPoint != null && connectResult;
        }

        /// <summary>
        /// Default timeout on a connection attempt it something around 15 seconds, which is ridiculously long.
        /// </summary>
        private void CancelConnect(object obj)
        {
            IAsyncResult result = (IAsyncResult) obj;
#if !UNITY_WINRT
            if (result != null && !result.AsyncWaitHandle.WaitOne(3000, true))
            {
                try
                {
                    Socket sock = (Socket) result.AsyncState;

                    if (sock != null)
                    {
                        sock.Close();

                        lock (connectingList)
                        {
                            // Last active connection attempt
                            if (connectingList.size > 0 && connectingList[connectingList.size - 1] == sock)
                            {
                                socket = null;

                                if (!ConnectToFallback())
                                {
                                    Debug.LogError("[Client][TcpProtocol:ConnectToFallback()] -> Unable to connect");
                                    Error("Unable to connect");
                                    Close(false);
                                }
                            }

                            connectingList.Remove(sock);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
#endif
        }

        /// <summary>
        /// Connection attempt result.
        /// </summary>
        private void OnConnectResult(IAsyncResult result)
        {
            Socket sock = (Socket) result.AsyncState;

            // Windows handles async sockets differently than other platforms, it seems.
            // If a socket is closed, OnConnectResult() is never called on Windows.
            // On the mac it does get called, however, and if the socket is used here
            // then a null exception gets thrown because the socket is not usable by this point.
            if (sock == null)
            {
                Debug.LogError("[Client][TcpProtocol:OnConnectResult() -> (socket)result.AsyncState is null");
                return;
            }

            if (socket != null && sock == socket)
            {
                bool success = true;
                string errMsg = "Failed to connect";

                try
                {
#if !UNITY_WINRT
                    sock.EndConnect(result);
#endif
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);

                    if (sock == socket)
                    {
                        socket = null;
                    }
                    sock.Close();
                    errMsg = ex.Message;
                    success = false;
                }

                if (success)
                {
#if DEBUG
                    Debug.Log(
                        "[Client][TcpProtocol:OnConnectResult() -> Connetion successful. Sending request to verify ID.");
#endif
                    
                    Status = ConnectionStatus.Verifying;
                    
                    //Request a connection ID
                    Buffer requestIdBuffer = Buffer.CreatePackage(Packet.RequestID, out BinaryWriter packageWriter);
                    packageWriter.Write(VERSION);
                    requestIdBuffer.EndPacket();
                    SendTcpPacket(requestIdBuffer);
                    
                    StartReceiving();
                }
                else if (!ConnectToFallback())
                {
                    Debug.LogError("[Client][TcpProtocol:ConnectToFallback() -> " + errMsg);
                    Error(errMsg);
                    Close(false);
                }
            }

            // We are no longer trying to connect via this socket
            lock (connectingList)
            {
                connectingList.Remove(sock);
            }
        }

        /// <summary>
        /// Disconnect the instance, freeing all resources.
        /// </summary>
        public void Disconnect()
        {
            Disconnect(false);
        }

        /// <summary>
        /// Disconnect the instance, freeing all resources.
        /// </summary>
        public void Disconnect(bool notify)
        {
            if (!IsConnected)
            {
#if DEBUG
                Debug.LogWarning("[Client][TcpProtocol:Disconnect(" + notify + ") -> Not connected.");
#endif
                return;
            }

            try
            {
                lock (connectingList)
                {
                    //close all connections first
                    for (int i = connectingList.size; i > 0;)
                    {
                        Socket sock = connectingList[--i];
                        connectingList.RemoveAt(i);
                        sock?.Close();
                    }
                }

                // Stop the worker thread
                if (clientThread != null)
                {
                    clientThread.Abort();
                    clientThread = null;
                }

                if (socket != null)
                {
#if DEBUG
                    Debug.Log("[Client][TcpProtocol:Disconnect(" + notify + ") -> Disconnected.");
#endif
                    Close(notify || socket.Connected);
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                lock (connectingList)
                {
                    connectingList.Clear();
                }
                socket = null;
            }
        }

        /// <summary>
        /// Close the connection.
        /// </summary>
        public void Close(bool notify)
        {
            if (socket != null)
            {
                if (notify)
                {
#if DEBUG
                    Debug.Log("[Client][TcpProtocol:Close(" + notify + ") -> Sending disconnect message.");
#endif

                    Buffer buffer = Buffer.Create();
                    buffer.BeginPacket(Packet.Disconnect);
                    buffer.EndTcpPacketWithOffset(4);
                    lock (inQueue)
                    {
                        inQueue.Enqueue(buffer);
                    }
                }
                
                try
                {
                    if (socket.Connected)
                    {
                        socket.Shutdown(SocketShutdown.Both);
                    }

                    socket.Close();

#if DEBUG
                    Debug.Log("[Client][TcpProtocol:Close(" + notify + ") -> Closed.");
#endif
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
                socket = null;
            }
            
            Status = ConnectionStatus.NotConnected;

            //recycle buffer
            if (receiveBuffer != null)
            {
                receiveBuffer.Recycle();
                receiveBuffer = null;
            }

            // Stop the worker thread
            if (clientThread != null)
            {
                clientThread.Abort();
                clientThread = null;
            }
        }

        /// <summary>
        /// Release the buffers.
        /// </summary>
        public void Release(bool notify = false)
        {
#if DEBUG
            Debug.Log("[Client][TcpProtocol:Release() -> Released.");
#endif
            Close(notify);
            Buffer.Recycle(inQueue);
            Buffer.Recycle(outQueue);
        }
        
        /// <summary>
        /// Send the specified packet. Marks the buffer as used.
        /// </summary>
        public void SendTcpPacket(Buffer buffer)
        {
            buffer.MarkAsUsed();

            if (socket != null && socket.Connected)
            {
                buffer.BeginReading();

                lock (outQueue)
                {
                    outQueue.Enqueue(buffer);

                    if (outQueue.Count == 1)
                    {
                        try
                        {
#if DEBUG
                            Debug.Log("[Client][TcpProtocol:SendTcpPacket(" + buffer.Size + ") -> Sending packed.");
#endif
                            // If it's the first packet, let's begin the send process
#if !UNITY_WINRT
                            socket.BeginSend(buffer.DataBuffer, buffer.Position, buffer.Size, SocketFlags.None, OnSend,
                                buffer);
#endif
                        }
                        catch (Exception ex)
                        {
                            Debug.LogException(ex);
                            Error(ex.Message);
                            Close(false);
                            Release();
                        }
                    }
                }
            }
            else
            {
#if DEBUG
                Debug.LogWarning("[Client][TcpProtocol:SendTcpPacket(" + buffer.Size +
                                 ") -> Socket is null or not connected.");
#endif
                buffer.Recycle();
            }
        }

        /// <summary>
        /// Send completion callback. Recycles the buffer.
        /// </summary>
        private void OnSend(IAsyncResult result)
        {
            if (Status == ConnectionStatus.NotConnected)
            {
#if DEBUG
                Debug.LogWarning("[Client][TcpProtocol:OnSend() -> Not connected.");
#endif
                return;
            }

            int bytes;

            try
            {
#if DEBUG
                Debug.Log("[Client][TcpProtocol:OnSend() -> End send successful.");
#endif
#if !UNITY_WINRT
                bytes = socket.EndSend(result);
#endif
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                bytes = 0;
                Close(true);
                Error(ex.Message);
                return;
            }

            lock (outQueue)
            {
                // The buffer has been sent and can now be safely recycled
                outQueue.Dequeue().Recycle();
#if !UNITY_WINRT
                if (bytes > 0 && socket != null && socket.Connected)
                {
                    // If there is another packet to send out, let's send it
                    Buffer next = outQueue.Count == 0 ? null : outQueue.Peek();

                    if (next != null)
                    {
                        try
                        {
                            socket.BeginSend(next.DataBuffer, next.Position, next.Size, SocketFlags.None, OnSend, next);
#if DEBUG
                            Debug.Log("[Client][TcpProtocol:OnSend() -> Sending another packet.");
#endif
                        }
                        catch (Exception ex)
                        {
                            Debug.LogException(ex);
                            Error(ex.Message);
                            Close(false);
                        }
                    }
                }
                else
                {
#if DEBUG
                    Debug.LogWarning("[Client][TcpProtocol:OnSend() -> Socket is null.");
#endif
                    Close(true);
                }
#endif
            }
        }

        /// <summary>
        /// Start receiving incoming messages on the current socket.
        /// </summary>
        public void StartReceiving()
        {
            StartReceiving(null);
        }

        /// <summary>
        /// Start receiving incoming messages on the specified socket (for example socket accepted via Listen).
        /// </summary>
        public void StartReceiving(Socket socket)
        {
            if (socket != null)
            {
                Close(false);
                this.socket = socket;

#if DEBUG
                Debug.Log("[Client][TcpProtocol:StartReceiving() -> Changing socket.");
#endif
            }

            if (this.socket != null && this.socket.Connected)
            {
                // We are not verifying the connection
                Status = ConnectionStatus.Verifying;

                // Save the timestamp
                LastReceivedTime = DateTime.UtcNow.Ticks / 10000;

                // Save the address
                EndPoint = (IPEndPoint) this.socket.RemoteEndPoint;

#if MULTI_THREADED
                clientThread = new Thread(ThreadProcessListenerPackets);
                clientThread.Start();
#endif

                // Queue up the read operation
                try
                {
#if !UNITY_WINRT
                    this.socket.BeginReceive(temp, 0, temp.Length, SocketFlags.None, OnReceive, this.socket);
#endif
#if DEBUG
                    Debug.Log("[Client][TcpProtocol:StartReceiving() -> Begin receive.");
#endif
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Error(ex.Message);
                    Disconnect(true);
                }
            }
            else
            {
#if DEBUG
                Debug.LogWarning("[Client][TcpProtocol:StartReceiving() -> Socket is null or not connected.");
#endif
            }
        }

        /// <summary>
        /// Extract the first incoming packet.
        /// </summary>
        public bool ReceivePacket(out Buffer buffer)
        {
            if (inQueue.Count != 0)
            {
                lock (inQueue)
                {
                    buffer = inQueue.Dequeue();
#if DEBUG
                    Debug.Log("[Client][TcpProtocol:ReceivePacket(" + buffer.Size + ")] - Receiving packet ...");
#endif
                    return true;
                }
            }

            buffer = null;
            return false;
        }

        /// <summary>
        /// Receive incoming data.
        /// </summary>
        private void OnReceive(IAsyncResult result)
        {
            if (Status == ConnectionStatus.NotConnected)
            {
#if DEBUG
                Debug.LogWarning("[Client][TcpProtocol:OnReceive() -> Not connected.");
#endif
                return;
            }

            int bytes = 0;
            Socket socket = (Socket) result.AsyncState;

            try
            {
#if DEBUG
                Debug.Log("[Client][TcpProtocol:OnReceive() -> EndReceive.");
#endif
#if !UNITY_WINRT
                bytes = socket.EndReceive(result);
#endif
                if (this.socket != socket)
                {
#if DEBUG
                    Debug.LogWarning(
                        "[Client][TcpProtocol:OnReceive() -> Current socket is not equals (Socket)result.AsyncState.");
#endif
                    return;
                }
            }
            catch (Exception ex)
            {
                if (this.socket != socket)
                {
#if DEBUG
                    Debug.LogWarning(
                        "[Client][TcpProtocol:OnReceive() -> Current socket is not equals (Socket)result.AsyncState.");
#endif
                    return;
                }

                Debug.LogException(ex);
                Error(ex.Message);
                Disconnect(true);
                return;
            }

            LastReceivedTime = DateTime.UtcNow.Ticks / 10000;

            if (bytes == 0)
            {
#if DEBUG
                Debug.LogWarning("[Client][TcpProtocol:OnReceive() -> Bytes received is 0.");
#endif
                Close(true);
            }
            else if (ProcessBuffer(bytes))
            {
                if (Status == ConnectionStatus.NotConnected)
                {
#if DEBUG
                    Debug.LogWarning("[Client][TcpProtocol:OnReceive() -> Not connected.");
#endif
                    return;
                }

                try
                {
#if !UNITY_WINRT
                    // Queue up the next read operation
                    socket.BeginReceive(temp, 0, temp.Length, SocketFlags.None, OnReceive, socket);
#endif
#if DEBUG
                    Debug.Log("[Client][TcpProtocol:OnReceive() -> Begin receive again.");
#endif
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Error(ex.Message);
                    Close(false);
                }
            }
            else
            {
#if DEBUG
                Debug.Log("[Client][TcpProtocol:OnReceive() -> ???");
#endif
                Close(true);
            }
        }

        /// <summary>
        /// See if the received packet can be processed and split it up into different ones.
        /// </summary>
        private bool ProcessBuffer(int bytes)
        {
            if (receiveBuffer == null)
            {
                // Create a new packet buffer
                receiveBuffer = Buffer.Create();
                receiveBuffer.BeginWriting(false).Write(temp, 0, bytes);
                expected = 0;
                offset = 0;
#if DEBUG
                Debug.Log("[Client][TcpProtocol:ProcessBuffer(" + bytes +
                          ") -> ReceivedBuffer is null, then creating new buffer.");
#endif
            }
            else
            {
                // Append this data to the end of the last used buffer
                receiveBuffer.BeginWriting(true).Write(temp, 0, bytes);
#if DEBUG
                Debug.Log("[Client][TcpProtocol:ProcessBuffer(" + bytes + ") -> Appending ReceivedBuffer.");
#endif
            }

            for (int available = receiveBuffer.Size - offset; available >= 4;)
            {
                // Figure out the expected size of the packet
                if (expected == 0)
                {
                    expected = receiveBuffer.PeekInt(offset);

                    if (expected < 0 || expected > 16777216)
                    {
#if DEBUG
                        Debug.Log("[Client][TcpProtocol:ProcessBuffer(" + bytes + ") -> ???");
#endif
                        Close(true);
                        return false;
                    }
                }

                // The first 4 bytes of any packet always contain the number of bytes in that packet
                available -= 4;

                // If the entire packet is present
                if (available == expected)
                {
                    // Reset the position to the beginning of the packet
                    receiveBuffer.BeginReading(offset + 4);

                    // This packet is now ready to be processed
                    lock (inQueue)
                    {
                        inQueue.Enqueue(receiveBuffer);
                    }

                    receiveBuffer = null;
                    expected = 0;
                    offset = 0;
#if DEBUG
                    Debug.Log("[Client][TcpProtocol:ProcessBuffer(" + bytes + ") -> Entire packet is present.");
#endif
                    break;
                }
                if (available > expected)
                {
                    // There is more than one packet. Extract this packet fully.
                    int realSize = expected + 4;
#if DEBUG
                    Debug.Log("[Client][TcpProtocol:ProcessBuffer(" + bytes +
                              ") -> There is more than one packet. Extract this packet fully. RealSize = " + realSize +
                              " | available(" + available + ") > mExpected(" + expected + ")");
#endif
                    Buffer temp = Buffer.Create();

                    // Extract the packet and move past its size component
                    BinaryWriter bw = temp.BeginWriting(false);
                    bw.Write(receiveBuffer.DataBuffer, offset, realSize);
                    temp.BeginReading(4);

                    // This packet is now ready to be processed
                    lock (inQueue)
                    {
                        inQueue.Enqueue(temp);
                    }

                    // Skip this packet
                    available -= expected;
                    offset += realSize;
                    expected = 0;
                }
                else
                {
#if DEBUG
                    Debug.Log("[Client][TcpProtocol:ProcessBuffer(" + bytes + ") -> available(" + available +
                              ") < mExpected(" + expected + ")");
#endif
                    break;
                }
            }

            return true;
        }

        /// <summary>
        /// Add an error packet to the incoming queue.
        /// </summary>
        public void Error(string error)
        {
            Error(Buffer.Create(), error);
        }

        /// <summary>
        /// Add an error packet to the incoming queue.
        /// </summary>
        private void Error(Buffer buffer, string error)
        {
            buffer.BeginPacket(Packet.Error).Write(error);
            buffer.EndTcpPacketWithOffset(4);
            lock (inQueue)
            {
                inQueue.Enqueue(buffer);
            }
        }

        /// <summary>
        /// Verify the connection.
        /// </summary>
        public bool VerifyRequestID(Buffer buffer, bool uniqueID)
        {
            BinaryReader reader = buffer.BeginReading();
            Packet request = (Packet) reader.ReadByte();

            if (request == Packet.RequestID)
            {
                if (reader.ReadInt32() == VERSION)
                {
                    lock (listenerLockObj)
                    {
                        Id = uniqueID ? ++connectionCounter : 0;
                    }

                    Status = ConnectionStatus.Connected;

                    Buffer responseIdBuffer = Buffer.CreatePackage(Packet.ResponseID, out BinaryWriter packageWriter);
                    packageWriter.Write(VERSION);
                    packageWriter.Write(Id);
                    packageWriter.Write(DateTime.UtcNow.Ticks / 10000);
                    responseIdBuffer.EndPacket();
                    SendTcpPacket(responseIdBuffer);
#if DEBUG
                    Debug.Log("[Client][TcpProtocol:VerifyRequestID()] -> Protocol marked as connected in server. !");
#endif
                    return true;
                }
                else
                {
                    Buffer responseIdBuffer = Buffer.CreatePackage(Packet.ResponseID, out BinaryWriter packageWriter);
                    packageWriter.Write(0);
                    responseIdBuffer.EndPacket();
                    SendTcpPacket(responseIdBuffer);
#if DEBUG
                    Debug.LogWarning("[Client][TcpProtocol:VerifyRequestID()] -> Incorrect version.");
#endif
                    Close(false);
                }
            }
            else
            {
#if DEBUG
                Debug.LogWarning("[Client][TcpProtocol:VerifyRequestID(" + buffer.Size + ", " + uniqueID +
                                 ") -> Packet is not Packet.RequestID");
#endif
            }

            return false;
        }

        /// <summary>
        /// Verify the connection.
        /// </summary>
        public bool VerifyResponseID(Packet packet, BinaryReader reader)
        {
            if (packet == Packet.ResponseID)
            {
                int serverVersion = reader.ReadInt32();

                if (serverVersion != 0 && serverVersion == VERSION)
                {
                    Id = reader.ReadInt32();
                    Status = ConnectionStatus.Connected;

#if DEBUG
                    Debug.Log("[Client][TcpProtocol:VerifyRequestID()] -> Protocol marked as connected in client. !");
#endif

                    return true;
                }
                
                Id = 0;
                Debug.LogError(
                    "[Client][TcpProtocol:VerifyResponseID() -> Version mismatch! Server is running a different protocol version!");
                Error("Version mismatch! Server is running a different protocol version!");
                Close(false);

                return false;
            }

            Debug.LogError("[Client][TcpProtocol:VerifyResponseID() -> Expected a response ID, got " + packet);
            Error("Expected a response ID, got " + packet);
            Close(false);

            return false;
        }

        /// <summary>
        /// Call after shutting down the listener.
        /// </summary>
        public void ResetConnectionsCounter()
        {
#if DEBUG
            Debug.Log("[Client][TcpProtocol:ResetConnectionsCounter()] - Reseted.");
#endif
            connectionCounter = 0;
        }

        /// <summary>
        /// Process a single incoming packet. Returns whether we should keep processing packets or not.
        /// </summary>
        private bool ProcessListenerPacket(Buffer buffer, IPEndPoint ip)
        {
#if DEBUG
            Debug.Log("[Client][TcpProtocol:ProcessListenerPacket()] - Processing. Status: " + Status);
#endif

            // Verification step must be passed first
            if (Status == ConnectionStatus.Verifying)
            {
                BinaryReader reader = buffer.BeginReading();
                if (buffer.Size == 0)
                {
                    return true;
                }

                int packetID = reader.ReadByte();
                Packet response = (Packet) packetID;

                if (response == Packet.ResponseID)
                {
                    if (VerifyResponseID(response, reader))
                    {
#if DEBUG
                        Debug.Log("[Client][TcpProtocol:ProcessListenerPacket()] - Verified. Id: " + Id);
#endif
                        return true;
                    }
#if DEBUG
                    Debug.Log("[Client][TcpProtocol:ProcessListenerPacket()] - Not Verified.");
#endif
                    return false;
                }
            }
            else if (Status == ConnectionStatus.Connected)
            {
#if DEBUG
                Debug.Log("[Client][TcpProtocol:ProcessListenerPacket()] - Packet received.");
#endif
                OnClientPacketReceived?.Invoke(buffer, ip);
            }
#if DEBUG
            Debug.Log("[Client][TcpProtocol:ProcessListenerPacket()] - Processed. Status: " + Status);
#endif
            return true;
        }

        /// <summary>
        /// Process all incoming packets.
        /// </summary>
        public void ThreadProcessListenerPackets()
        {
#if MULTI_THREADED
            for (;;)
#endif
            {
#if DEBUG
                //Debug.Log("[Client] - Running ThreadProcessListenerPackets.");
#endif
                bool received = false;

                lock (listenerLockObj)
                {
                    Buffer buffer = null;

                    //receive all packets
                    while (ReceivePacket(out buffer))
                    {
                        try
                        {
                            received = ProcessListenerPacket(buffer, null);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogException(ex);
                        }

                        buffer.Recycle();
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
        /// Process incoming packets in the Unity Update function.
        /// </summary>
        public void UpdateClient()
        {
            if (clientThread == null && socket != null)
            {
                ThreadProcessListenerPackets();
            }
        }

        public bool CheckClientThread()
        {
            return clientThread != null && clientThread.IsAlive;
        }

        #endregion

        #region TcpListener Methods

        /// <summary>
        /// Start listening to incoming connections on the specified port.
        /// </summary>
        public bool StartListener(int tcpPort)
        {
            StopListener();

            try
            {
                listenerPort = tcpPort;
                listener = new TcpListener(IPAddress.Any, tcpPort);
                listener.Start(50);
            }
            catch (Exception ex)
            {
                Error(ex.Message);
                return false;
            }
#if DEBUG
            Debug.Log("[Listener][TcpProtocol:StartListener()] - Game server started on port " + tcpPort +
                      " using protocol version " + VERSION);
#endif

#if MULTI_THREADED
            listenerThread = new Thread(ThreadProcessClientPackets);
            listenerThread.Start();
#endif
            return true;
        }

        /// <summary>
        /// Stop listening to incoming connections and disconnect all players.
        /// </summary>
        public void StopListener()
        {
#if DEBUG
            Debug.Log("[Listener][TcpProtocol:StopListener()] - Stopped.");
#endif

            // Stop the worker thread
            if (listenerThread != null)
            {
                listenerThread.Abort();
                listenerThread = null;
            }

            // Stop listening
            if (listener != null)
            {
                listener.Stop();
                listener = null;
            }

            // Player counter should be reset
            ResetConnectionsCounter();
        }

        /// <summary>
        /// Stop listening to incoming connections but keep the server running.
        /// </summary>
        public void RefuseConnections()
        {
            listenerPort = 0;
        }

        /// <summary>
        /// Thread that will be processing incoming data.
        /// </summary>
        private void ThreadProcessClientPackets()
        {
#if MULTI_THREADED
            for (;;)
#endif
            {
                bool received = false;

                lock (listenerLockObj)
                {
                    Buffer buffer;
                    time = DateTime.UtcNow.Ticks / 10000;

                    // Stop the listener if the port is 0 (MakePrivate() was called)
                    if (listenerPort == 0)
                    {
                        if (listener != null)
                        {
                            listener.Stop();
                            listener = null;
                        }
                    }
                    else
                    {
                        // Add all pending connections
                        while (listener != null && listener.Pending())
                        {
                            AddClient(listener.AcceptSocket());
                        }
                    }
#if DEBUG
                    //Debug.Log("[Listener] - Running ThreadProcessClientPackets. Clients: " + clients.size);
#endif
                    // Process player connections next
                    for (int i = 0; i < clients.size;)
                    {
                        TcpProtocol client = clients[i];

                        // Process up to 100 packets at a time
                        for (int b = 0; b < 100 && client.ReceivePacket(out buffer); ++b)
                        {
                            if (buffer.Size > 0)
                            {
                                try
                                {
                                    if (ProcessClientPacket(buffer, client))
                                    {
                                        received = true;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogException(ex);
                                    Error("(Listener ThreadFunction Process) " + ex.Message + "\n" + ex.StackTrace);
                                    RemoveClient(client);
                                }
                            }

                            buffer.Recycle();
                        }

                        // Time out -- disconnect this player
                        if (client.Status == ConnectionStatus.Connected)
                        {
                            // If the player doesn't send any packets in a while, disconnect him
                            if (client.TimeoutTime > 0 && client.LastReceivedTime + client.TimeoutTime < time)
                            {
#if DEBUG
                                Debug.LogWarning("[TcpProtocol:StopListener()] - Client " + client.Address +
                                                 " has timed out");
#endif
                                RemoveClient(client);
                                continue;
                            }
                        }
                        else if (client.LastReceivedTime + 2000 < time)
                        {
#if DEBUG
                            Debug.LogWarning("[TcpProtocol:StopListener()] - Client " + client.Address +
                                             " has timed out");
#endif
                            RemoveClient(client);
                            continue;
                        }
                        ++i;
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
        public void UpdateListener()
        {
            if (listenerThread == null && listener != null)
            {
                ThreadProcessClientPackets();
            }
        }

        /// <summary>
        /// Add a new player entry.
        /// </summary>
        private TcpProtocol AddClient(Socket socket)
        {
            TcpProtocol client = new TcpProtocol();
            client.StartReceiving(socket);
            clients.Add(client);
            return client;
        }

        /// <summary>
        /// Remove the specified player.
        /// </summary>
        private void RemoveClient(TcpProtocol client, bool notify = false)
        {
            if (client != null)
            {
                OnTcpClientDisconnect?.Invoke(client);
                client.Release(notify);
                clients.Remove(client);
                if (clientsDictionary.ContainsKey(client.Id))
                {
                    clientsDictionary.Remove(client.Id);
                }
            }
        }

        public void KickClient(int id)
        {
            if (clientsDictionary.TryGetValue(id, out TcpProtocol p))
            {
                RemoveClient(p, false);
            }
        }

        /// <summary>
        /// Retrieve a player by their ID.
        /// </summary>
        public TcpProtocol GetClient(int id)
        {
            clientsDictionary.TryGetValue(id, out TcpProtocol p);
            return p;
        }

        /// <summary>
        /// Send a buffer to player by ID
        /// </summary>
        public void SendToClient(int index, Buffer buffer)
        {
            clients[index].SendTcpPacket(buffer);
        }
        
        /// <summary>
        /// Send a buffer to player by ID
        /// </summary>
        public void SendToClientById(int clientId, Buffer buffer)
        {
            clientsDictionary[clientId].SendTcpPacket(buffer);
        }

        /// <summary>
        /// Receive and process a single incoming packet.
        /// Returns 'true' if a packet was received, 'false' otherwise.
        /// </summary>
        private bool ProcessClientPacket(Buffer buffer, TcpProtocol client)
        {
#if DEBUG
            Debug.Log("[Listener][TcpProtocol:ProcessClientPacket()] - Processing. Status: " + client.Status);
#endif
            // If the player has not yet been verified, the first packet must be an ID request
            if (client.Status == ConnectionStatus.Verifying)
            {
                if (client.VerifyRequestID(buffer, true))
                {
#if DEBUG
                    Debug.Log("[Listener][TcpProtocol:ProcessClientPacket()] - Client verified. Id: " + client.Id);
#endif
                    clientsDictionary.Add(client.Id, client);
                    OnTcpClientConnect?.Invoke(client);
                    return true;
                }

                RemoveClient(client);
                return false;
            }
            if (client.Status == ConnectionStatus.Connected)
            {
                Debug.Log("[Listener][TcpProtocol:ProcessClientPacket()] - Packet received.");

                OnListenerPacketReceived?.Invoke(buffer, client.EndPoint);
            }
            
            return true;
        }

        #endregion
    }
}