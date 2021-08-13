using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
#if UNITY_IPHONE || !UNITY_WEBPLAYER
using System.Net.NetworkInformation;

#endif

namespace LegendaryTools.Networking
{
    /// <summary>
    /// Generic sets of helper functions used within network system.
    /// </summary>
    public static class NetworkUtility
    {
        public delegate void OnResolvedIPs(IPAddress local, IPAddress ext);

        private static string mChecker;

        private static IPAddress mLocalAddress;
        private static IPAddress mExternalAddress;

        /// <summary>
        /// Whether the external IP address is reliable. It's set to 'true' when it gets resolved successfully.
        /// </summary>
        public static bool isExternalIPReliable;

        private static ListLessGarb<IPAddress> mAddresses;
        private static OnResolvedIPs mOnResolve;
        private static Thread mResolveThread;

        /// <summary>
        /// Application directory to use in My Documents. Generally should be the name of your game.
        /// </summary>
        public static string applicationDirectory = null;

        /// <summary>
        /// Get or set the URL that will perform the IP check. The URL-returned value should be simply the IP address:
        /// "255.255.255.255".
        /// Note that the server must have a valid policy XML if it's accessed from a Unity web player build.
        /// </summary>

        public static string ipCheckerUrl
        {
            get => mChecker;
            set
            {
                if (mChecker != value)
                {
                    mChecker = value;
                    mLocalAddress = null;
                    mExternalAddress = null;
                }
            }
        }

        /// <summary>
        /// Generate a random port from 10,000 to 60,000.
        /// </summary>

        public static int randomPort => 10000 + (int) (DateTime.UtcNow.Ticks % 50000);

        /// <summary>
        /// Return the list of local addresses. There can be more than one if there is more than one network (for example: Hamachi).
        /// </summary>

        public static ListLessGarb<IPAddress> localAddresses
        {
            get
            {
                if (mAddresses == null)
                {
                    mAddresses = new ListLessGarb<IPAddress>();
#if !UNITY_WEBPLAYER && !UNITY_WINRT
                    try
                    {
                        ListLessGarb<NetworkInterface> list = networkInterfaces;

                        for (int i = list.size; i > 0;)
                        {
                            NetworkInterface ni = list[--i];
                            if (ni == null)
                            {
                                continue;
                            }

                            IPInterfaceProperties props = ni.GetIPProperties();
                            if (props == null)
                            {
                                continue;
                            }
                            //if (ni.NetworkInterfaceType == NetworkInterfaceType.Unknown) continue;

                            UnicastIPAddressInformationCollection uniAddresses = props.UnicastAddresses;
                            if (uniAddresses == null)
                            {
                                continue;
                            }

                            foreach (UnicastIPAddressInformation uni in uniAddresses)
                            {
                                // BUG: Accessing 'uni.Address' crashes when executed in a stand-alone build in Unity,
                                // yet works perfectly fine when launched from within the Unity Editor or any other platform.
                                // The stack trace reads:
                                //
                                // Argument cannot be null. Parameter name: src
                                // at (wrapper managed-to-native) System.Runtime.InteropServices.Marshal:PtrToStructure (intptr,System.Type)
                                // at System.Net.NetworkInformation.Win32_SOCKET_ADDRESS.GetIPAddress () [0x00000] in <filename unknown>:0 
                                // at System.Net.NetworkInformation.Win32UnicastIPAddressInformation.get_Address () [0x00000] in <filename unknown>:0

                                if (IsValidAddress(uni.Address))
                                {
                                    mAddresses.Add(uni.Address);
                                }
                            }
                        }

                        Debug.Log("[Tools:networkInterfaces] 1 localAddresses Amount: " + mAddresses.Count);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
#endif
#if !UNITY_IPHONE && !UNITY_EDITOR_OSX && !UNITY_STANDALONE_OSX && !UNITY_WINRT
                    // Fallback method. This won't work on the iPhone, but seems to be needed on some platforms
                    // where GetIPProperties either fails, or Unicast.Addres access throws an exception.
                    string hn = Dns.GetHostName();

                    if (!string.IsNullOrEmpty(hn))
                    {
                        IPAddress[] ips = Dns.GetHostAddresses(hn);

                        if (ips != null)
                        {
                            foreach (IPAddress ad in ips)
                            {
                                if (IsValidAddress(ad) && !mAddresses.Contains(ad))
                                {
                                    mAddresses.Add(ad);
                                }
                            }
                        }
                    }

                    Debug.Log("[Tools:networkInterfaces] 2 localAddresses Amount: " + mAddresses.Count);
#endif
                    // If everything else fails, simply use the loopback address
                    if (mAddresses.size == 0)
                    {
                        mAddresses.Add(IPAddress.Loopback);
                    }
                }

                Debug.Log("[Tools:networkInterfaces] 3 localAddresses Amount: " + mAddresses.Count);

                return mAddresses;
            }
        }

        /// <summary>
        /// Default local IP address. Note that there can be more than one address in case of more than one network.
        /// </summary>

        public static IPAddress localAddress
        {
            get
            {
                if (mLocalAddress == null)
                {
                    mLocalAddress = IPAddress.Loopback;
                    ListLessGarb<IPAddress> list = localAddresses;

                    if (list.size > 0)
                    {
                        mLocalAddress = mAddresses[0];

                        for (int i = 0; i < mAddresses.size; ++i)
                        {
                            IPAddress addr = mAddresses[i];
                            string str = addr.ToString();

                            // Hamachi IPs begin with 25
                            if (str.StartsWith("25."))
                            {
                                continue;
                            }

                            // This is a valid address
                            mLocalAddress = addr;
                            break;
                        }
                    }
                }
                return mLocalAddress;
            }
            set
            {
                mLocalAddress = value;

                if (value != null)
                {
                    //check is value is a valid address
                    ListLessGarb<IPAddress> list = localAddresses;
                    for (int i = 0; i < list.size; ++i)
                    {
                        if (list[i] == value)
                        {
                            return;
                        }
                    }
                }
#if UNITY_EDITOR
                Debug.LogWarning("[network system] " + value +
                                 " is not one of the local IP addresses. Strange things may happen.");
#else
			System.Console.WriteLine("[network system] " + value + " is not one of the local IP addresses. Strange things may happen.");
#endif
            }
        }

        /// <summary>
        /// Immediately retrieve the external IP address.
        /// Note that if the external address is not yet known, this operation may hold up the application.
        /// </summary>

        public static IPAddress externalAddress
        {
            get
            {
                if (mExternalAddress == null)
                {
                    mExternalAddress = GetExternalAddress();
                }
                return mExternalAddress != null ? mExternalAddress : localAddress;
            }
        }

        /// <summary>
        /// Since calling "localAddress" and "externalAddress" would lock up the application, it's better to do it asynchronously.
        /// </summary>
        public static void ResolveIPs()
        {
            ResolveIPs(null);
        }

        /// <summary>
        /// Since calling "localAddress" and "externalAddress" would lock up the application, it's better to do it asynchronously.
        /// </summary>
        public static void ResolveIPs(OnResolvedIPs del)
        {
            if (isExternalIPReliable)
            {
                if (del != null)
                {
                    del(localAddress, externalAddress);
                }
            }
            else
            {
                if (mOnResolve == null)
                {
                    mOnResolve = ResolveDummyFunc;
                }

                lock (mOnResolve)
                {
                    if (del != null)
                    {
                        mOnResolve += del;
                    }

                    if (mResolveThread == null)
                    {
                        mResolveThread = new Thread(ResolveThread);
                        mResolveThread.Start();
                    }
                }
            }
        }

        private static void ResolveDummyFunc(IPAddress a, IPAddress b)
        {
        }

        /// <summary>
        /// Thread function that resolves IP addresses.
        /// </summary>
        private static void ResolveThread()
        {
            IPAddress local = localAddress;
            IPAddress ext = externalAddress;

            lock (mOnResolve)
            {
                if (mOnResolve != null)
                {
                    mOnResolve(local, ext);
                }
                mResolveThread = null;
                mOnResolve = null;
            }
        }

        /// <summary>
        /// Determine the external IP address by accessing an external web site.
        /// </summary>
        private static IPAddress GetExternalAddress()
        {
            if (mExternalAddress != null)
            {
                return mExternalAddress;
            }

#if UNITY_WEBPLAYER
		// HttpWebRequest.Create is not supported in the Unity web player
		return localAddress;
#else
            if (ResolveExternalIP(ipCheckerUrl))
            {
                return mExternalAddress;
            }
            if (ResolveExternalIP("http://icanhazip.com"))
            {
                return mExternalAddress;
            }
            if (ResolveExternalIP("http://bot.whatismyipaddress.com"))
            {
                return mExternalAddress;
            }
            if (ResolveExternalIP("http://ipinfo.io/ip"))
            {
                return mExternalAddress;
            }
#if UNITY_EDITOR
            Debug.LogWarning("Unable to resolve the external IP address via " + mChecker);
#endif
            return localAddress;
#endif
        }

        /// <summary>
        /// Resolve the external IP using the specified URL.
        /// </summary>
        private static bool ResolveExternalIP(string url)
        {
#if !UNITY_WINRT
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            try
            {
                WebClient web = new WebClient();
                string text = web.DownloadString(url).Trim();
                string[] split1 = text.Split(':');

                if (split1.Length >= 2)
                {
                    string[] split2 = split1[1].Trim().Split('<');
                    mExternalAddress = ResolveAddress(split2[0]);
                }
                else
                {
                    mExternalAddress = ResolveAddress(text);
                }

                if (mExternalAddress != null)
                {
                    isExternalIPReliable = true;
                    return true;
                }
            }
            catch (Exception)
            {
            }
#endif
            return false;
        }

        /// <summary>
        /// Helper function that determines if this is a valid address.
        /// </summary>
        public static bool IsValidAddress(IPAddress address)
        {
            if (address.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }
            if (address.Equals(IPAddress.Loopback))
            {
                return false;
            }
            if (address.Equals(IPAddress.None))
            {
                return false;
            }
            if (address.Equals(IPAddress.Any))
            {
                return false;
            }
            if (address.ToString().StartsWith("169."))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Helper function that resolves the remote address.
        /// </summary>
        public static IPAddress ResolveAddress(string address)
        {
            address = address.Trim();
            if (string.IsNullOrEmpty(address))
            {
                return null;
            }

            if (address == "localhost")
            {
                return IPAddress.Loopback;
            }

            IPAddress ip;

            if (address.Contains(":"))
            {
                string[] parts = address.Split(':');

                if (parts.Length == 2)
                {
                    if (IPAddress.TryParse(parts[0], out ip))
                    {
                        return ip;
                    }
                }
            }

            if (IPAddress.TryParse(address, out ip))
            {
                return ip;
            }

#if !UNITY_WINRT
            try
            {
                IPAddress[] ips = Dns.GetHostAddresses(address);

                for (int i = 0; i < ips.Length; ++i)
                {
                    if (!IPAddress.IsLoopback(ips[i]))
                    {
                        return ips[i];
                    }
                }
            }
#if UNITY_EDITOR
            catch (Exception ex)
            {
                Debug.LogWarning(ex.Message + " (" + address + ")");
            }
#else
		catch (System.Exception) {}
#endif
#endif
            return null;
        }

        /// <summary>
        /// Given the specified address and port, get the end point class.
        /// </summary>
        public static IPEndPoint ResolveEndPoint(string address, int port)
        {
            IPEndPoint ip = ResolveEndPoint(address);
            if (ip != null)
            {
                ip.Port = port;
            }
            return ip;
        }

        /// <summary>
        /// Given the specified address, get the end point class.
        /// </summary>
        public static IPEndPoint ResolveEndPoint(string address)
        {
            int port = 0;
            string[] split = address.Split(':');

            // Automatically try to parse the port
            if (split.Length > 1)
            {
                address = split[0];
                int.TryParse(split[1], out port);
            }

            IPAddress ad = ResolveAddress(address);
            return ad != null ? new IPEndPoint(ad, port) : null;
        }

        /// <summary>
        /// Converts 192.168.1.1 to 192.168.1.
        /// </summary>
        public static string GetSubnet(IPAddress ip)
        {
            if (ip == null)
            {
                return null;
            }
            string addr = ip.ToString();
            int last = addr.LastIndexOf('.');
            if (last == -1)
            {
                return null;
            }
            return addr.Substring(0, last);
        }

        /// <summary>
        /// Helper function that returns the response of the specified web request.
        /// </summary>
        public static string GetResponse(WebRequest request)
        {
            string response = "";

            try
            {
                WebResponse webResponse = request.GetResponse();
                Stream stream = webResponse.GetResponseStream();

                byte[] bytes = new byte[2048];

                for (;;)
                {
                    int count = stream.Read(bytes, 0, bytes.Length);
                    if (count > 0)
                    {
                        response += Encoding.ASCII.GetString(bytes, 0, count);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
            return response;
        }

        /// <summary>
        /// Serialize the IP end point.
        /// </summary>
        public static void Serialize(BinaryWriter writer, IPEndPoint ip)
        {
            byte[] bytes = ip.Address.GetAddressBytes();
            writer.Write((byte) bytes.Length);
            writer.Write(bytes);
            writer.Write((ushort) ip.Port);
        }

        /// <summary>
        /// Deserialize the IP end point.
        /// </summary>
        public static void Serialize(BinaryReader reader, out IPEndPoint ip)
        {
            byte[] bytes = reader.ReadBytes(reader.ReadByte());
            int port = reader.ReadUInt16();
            ip = new IPEndPoint(new IPAddress(bytes), port);
        }

        /// <summary>
        /// Write the channel's data into the specified writer.
        /// </summary>
        public static void Serialize(BinaryWriter writer, byte[] data)
        {
            int count = data != null ? data.Length : 0;

            if (count < 255)
            {
                writer.Write((byte) count);
            }
            else
            {
                writer.Write((byte) 255);
                writer.Write(count);
            }
            if (count > 0)
            {
                writer.Write(data);
            }
        }

        /// <summary>
        /// Read the channel's data from the specified reader.
        /// </summary>
        public static void Serialize(BinaryReader reader, out byte[] data)
        {
            int count = reader.ReadByte();
            if (count == 255)
            {
                count = reader.ReadInt32();
            }
            data = count > 0 ? reader.ReadBytes(count) : null;
        }

        /// <summary>
        /// Deserialize the IP end point.
        /// </summary>
        public static void Deserialize(BinaryReader reader, out IPEndPoint ip)
        {
            byte[] bytes = reader.ReadBytes(reader.ReadByte());
            int port = reader.ReadUInt16();
            ip = new IPEndPoint(new IPAddress(bytes), port);
        }

        /// <summary>
        /// Retrieve the list of filenames from the specified directory.
        /// </summary>
        public static string[] GetFiles(string directory, bool inMyDocuments = false)
        {
#if !UNITY_WEBPLAYER && !UNITY_FLASH && !UNITY_METRO && !UNITY_WP8 && !UNITY_WP_8_1
            try
            {
                if (inMyDocuments)
                {
                    directory = GetDocumentsPath(directory);
                }
                if (!Directory.Exists(directory))
                {
                    return null;
                }
                return Directory.GetFiles(directory);
            }
            catch (Exception)
            {
            }
#endif
            return null;
        }

        /// <summary>
        /// Write the specified file, creating all the subdirectories in the process.
        /// </summary>
        public static bool WriteFile(string path, byte[] data, bool inMyDocuments = false)
        {
#if !UNITY_WEBPLAYER && !UNITY_FLASH && !UNITY_METRO && !UNITY_WP8 && !UNITY_WP_8_1
            if (data == null || data.Length == 0)
            {
                return DeleteFile(path);
            }
            try
            {
                if (inMyDocuments)
                {
                    path = GetDocumentsPath(path);
                }
                string dir = Path.GetDirectoryName(path);

                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (File.Exists(path))
                {
                    FileAttributes att = File.GetAttributes(path);

                    if ((att & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        att = att & ~FileAttributes.ReadOnly;
                        File.SetAttributes(path, att);
                    }
                }

                File.WriteAllBytes(path, data);
                if (File.Exists(path))
                {
                    return true;
                }
#if !STANDALONE
                Debug.LogWarning("Unable to write " + path);
#endif
            }
#if STANDALONE
			catch (System.Exception) { }
#else
            catch (Exception ex)
            {
                Debug.LogError(ex.Message);
            }
#endif
#endif
            return false;
        }

        /// <summary>
        /// Read the specified file, returning all bytes read.
        /// </summary>
        public static byte[] ReadFile(string path)
        {
#if !UNITY_WEBPLAYER && !UNITY_FLASH && !UNITY_WINRT
            try
            {
                path = FindFile(path);
                if (!string.IsNullOrEmpty(path))
                {
                    return File.ReadAllBytes(path);
                }
            }
#if STANDALONE
		catch (System.Exception) { }
#else
            catch (Exception ex)
            {
                Debug.LogError(ex.Message);
            }
#endif
#endif
            return null;
        }

        /// <summary>
        /// Delete the specified file, if it exists.
        /// </summary>
        public static bool DeleteFile(string path)
        {
#if !UNITY_WEBPLAYER && !UNITY_FLASH && !UNITY_METRO && !UNITY_WP8 && !UNITY_WP_8_1
            try
            {
                path = FindFile(path);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
                return true;
            }
            catch (Exception)
            {
            }
#endif
            return false;
        }

        /// <summary>
        /// Gets the path to a file in My Documents or OSX equivalent.
        /// </summary>
        public static string GetDocumentsPath(string path = null)
        {
#if !UNITY_WEBPLAYER && !UNITY_FLASH && !UNITY_METRO && !UNITY_WP8 && !UNITY_WP_8_1
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(applicationDirectory))
            {
                docs = Path.Combine(docs, applicationDirectory);
            }
            return string.IsNullOrEmpty(path) ? docs : Path.Combine(docs, path);
#else
		return path;
#endif
        }

        /// <summary>
        /// Tries to find the specified file, checking the raw path, My Documents folder, and the application folder.
        /// Returns the path if found, null if not found.
        /// </summary>
        public static string FindFile(string path)
        {
#if !UNITY_WEBPLAYER && !UNITY_FLASH && !UNITY_METRO && !UNITY_WP8 && !UNITY_WP_8_1
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    return null;
                }
                if (File.Exists(path))
                {
                    return path;
                }
                path = GetDocumentsPath(path);
                if (File.Exists(path))
                {
                    return path;
                }
            }
            catch (Exception)
            {
            }
#endif
            return null;
        }

#if !UNITY_WEBPLAYER && !UNITY_WINRT
        private static ListLessGarb<NetworkInterface> mInterfaces;

        /// <summary>
        /// Return the list of operational network interfaces.
        /// </summary>

        public static ListLessGarb<NetworkInterface> networkInterfaces
        {
            get
            {
                if (mInterfaces == null)
                {
                    mInterfaces = new ListLessGarb<NetworkInterface>();
                    NetworkInterface[] list = NetworkInterface.GetAllNetworkInterfaces();

                    foreach (NetworkInterface ni in list)
                    {
                        if (ni.Supports(NetworkInterfaceComponent.IPv4) &&
                            (ni.OperationalStatus == OperationalStatus.Up ||
                             ni.OperationalStatus == OperationalStatus.Unknown))
                        {
                            mInterfaces.Add(ni);
                        }
                    }
                }

                Debug.Log("[Tools:networkInterfaces] Amount: " + mInterfaces.Count);

                return mInterfaces;
            }
        }
#endif
    }
}