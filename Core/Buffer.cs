using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = System.Object;

namespace LegendaryTools.Networking
{
    /// <summary>
    /// This class merges BinaryWriter and BinaryReader into one.
    /// </summary>
    public class Buffer
    {
        public const int SIZE_OFFSET = 0;
        public const int SIZEOF_SIZE = sizeof(int);
        
        private static readonly ListLessGarb<Buffer> pool = new ListLessGarb<Buffer>();

        private volatile int counter;
        private volatile bool inPool;
        private volatile BinaryReader binaryReader;
        private volatile int size;

        private volatile MemoryStream stream;
        private volatile BinaryWriter binaryWriter;
        private volatile bool isWriting;

        private Buffer()
        {
            stream = new MemoryStream();
            binaryWriter = new BinaryWriter(stream);
            binaryReader = new BinaryReader(stream);
        }

        /// <summary>
        /// The size of the data present in the buffer.
        /// </summary>

        public int Size => isWriting ? (int) stream.Position : size - (int) stream.Position;

        /// <summary>
        /// Position within the stream.
        /// </summary>

        public int Position
        {
            get => (int) stream.Position;
            set => stream.Seek(value, SeekOrigin.Begin);
        }

        /// <summary>
        /// Underlying memory stream.
        /// </summary>

        public MemoryStream Stream => stream;

        /// <summary>
        /// Get the entire buffer (note that it may be bigger than 'size').
        /// </summary>

        public byte[] DataBuffer => stream.GetBuffer();

        /// <summary>
        /// Number of buffers in the recycled list.
        /// </summary>

        public static int RecycleQueue => pool.size;

        ~Buffer()
        {
            stream.Dispose();
            stream = null;
        }

        /// <summary>
        /// Create a new buffer, reusing an old one if possible.
        /// </summary>
        public static Buffer Create()
        {
            return Create(true);
        }

        /// <summary>
        /// Create a new buffer, reusing an old one if possible.
        /// </summary>
        public static Buffer Create(bool markAsUsed)
        {
            Buffer b = null;

            if (pool.size == 0)
            {
                b = new Buffer();
            }
            else
            {
                lock (pool)
                {
                    if (pool.size != 0)
                    {
                        b = pool.Pop();
                        b.inPool = false;
                    }
                    else
                    {
                        b = new Buffer();
                    }
                }
            }
            b.counter = markAsUsed ? 1 : 0;
            return b;
        }
        
        public static Buffer CreatePackage(Packet type, out BinaryWriter packageWriter)
        {
            Buffer buffer = Create();
            packageWriter = buffer.BeginPacket(type);
            return buffer;
        }

        /// <summary>
        /// Release the buffer into the reusable pool.
        /// </summary>
        public bool Recycle()
        {
            lock (this)
            {
                if (inPool)
                {
                    // I really want to know if this ever happens
                    //throw new Exception("Releasing a buffer that's already in the pool");
                    return false;
                }
                if (--counter > 0)
                {
                    return false;
                }

                lock (pool)
                {
                    inPool = true;
                    Clear();
                    pool.Add(this);
                }
                return true;
            }
        }

        /// <summary>
        /// Recycle an entire queue of buffers.
        /// </summary>
        public static void Recycle(Queue<Buffer> list)
        {
            lock (pool)
            {
                while (list.Count != 0)
                {
                    Buffer b = list.Dequeue();
                    b.Clear();
                    pool.Add(b);
                }
            }
        }

        /// <summary>
        /// Recycle an entire queue of buffers.
        /// </summary>
        public static void Recycle(Queue<Datagram> list)
        {
            lock (pool)
            {
                while (list.Count != 0)
                {
                    Buffer b = list.Dequeue().buffer;
                    b.Clear();
                    pool.Add(b);
                }
            }
        }

        /// <summary>
        /// Recycle an entire list of buffers.
        /// </summary>
        public static void Recycle(ListLessGarb<Buffer> list)
        {
            lock (pool)
            {
                for (int i = 0; i < list.size; ++i)
                {
                    Buffer b = list[i];
                    b.Clear();
                    pool.Add(b);
                }
                list.Clear();
            }
        }

        /// <summary>
        /// Recycle an entire list of buffers.
        /// </summary>
        public static void Recycle(ListLessGarb<Datagram> list)
        {
            lock (pool)
            {
                for (int i = 0; i < list.size; ++i)
                {
                    Buffer b = list[i].buffer;
                    b.Clear();
                    pool.Add(b);
                }
                list.Clear();
            }
        }

        /// <summary>
        /// Mark the buffer as being in use.
        /// </summary>
        public void MarkAsUsed()
        {
            lock (this)
            {
                ++counter;
            }
        }

        /// <summary>
        /// Clear the buffer.
        /// </summary>
        public void Clear()
        {
            counter = 0;
            size = 0;
            if (stream.Capacity > 1024)
            {
                stream.SetLength(256);
            }
            stream.Seek(0, SeekOrigin.Begin);
            isWriting = true;
        }

        /// <summary>
        /// Copy the contents of this buffer into the target one, trimming away unused space.
        /// </summary>
        public void CopyTo(Buffer target)
        {
            BinaryWriter w = target.BeginWriting(false);
            int bytes = Size;
            if (bytes > 0)
            {
                w.Write(DataBuffer, Position, bytes);
            }
            target.EndWriting();
        }

        /// <summary>
        /// Begin the writing process.
        /// </summary>
        public BinaryWriter BeginWriting(bool append)
        {
            if (stream == null || !stream.CanWrite)
            {
                stream = new MemoryStream();
                binaryReader = new BinaryReader(stream);
                binaryWriter = new BinaryWriter(stream);
            }
            else if (!append || !isWriting)
            {
                stream.Seek(0, SeekOrigin.Begin);
                size = 0;
            }

            isWriting = true;
            return binaryWriter;
        }

        /// <summary>
        /// Begin the writing process, appending from the specified offset.
        /// </summary>
        public BinaryWriter BeginWriting(int startOffset)
        {
            if (stream == null || !stream.CanWrite)
            {
                stream = new MemoryStream();
                binaryReader = new BinaryReader(stream);
                binaryWriter = new BinaryWriter(stream);
            }
            else
            {
                stream.Seek(startOffset, SeekOrigin.Begin);
            }

            isWriting = true;
            return binaryWriter;
        }

        /// <summary>
        /// Finish the writing process, returning the packet's size.
        /// </summary>
        public int EndWriting()
        {
            if (isWriting)
            {
                size = Position;
                stream.Seek(0, SeekOrigin.Begin);
                isWriting = false;
            }
            return size;
        }

        /// <summary>
        /// Begin the reading process.
        /// </summary>
        public BinaryReader BeginReading()
        {
            if (isWriting)
            {
                isWriting = false;
                size = (int) stream.Position;
                stream.Seek(0, SeekOrigin.Begin);
            }
            return binaryReader;
        }

        /// <summary>
        /// Begin the reading process.
        /// </summary>
        public BinaryReader BeginReading(int startOffset)
        {
            if (isWriting)
            {
                isWriting = false;
                size = (int) stream.Position;
            }
            stream.Seek(startOffset, SeekOrigin.Begin);
            return binaryReader;
        }

        public Packet PeekPacket()
        {
            return (Packet) PeekByte(SIZEOF_SIZE);
        }
        
        /// <summary>
        /// Peek at the first byte at the specified offset.
        /// </summary>
        public byte PeekByte(int offset)
        {
            long pos = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            byte val = binaryReader.ReadByte();
            stream.Seek(pos, SeekOrigin.Begin);
            return val;
        }

        /// <summary>
        /// Peek at the first integer at the specified offset.
        /// </summary>
        public int PeekInt(int offset)
        {
            long pos = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            int val = binaryReader.ReadInt32();
            stream.Seek(pos, SeekOrigin.Begin);
            return val;
        }
        
        /// <summary>
        /// Peek at the first integer at the specified offset.
        /// </summary>
        public ushort PeekUInt16(int offset)
        {
            long pos = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            ushort val = binaryReader.ReadUInt16();
            stream.Seek(pos, SeekOrigin.Begin);
            return val;
        }

        /// <summary>
        /// Peek-read the specified number of bytes.
        /// </summary>
        public byte[] PeekBytes(int offset, int length)
        {
            long pos = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            byte[] bytes = binaryReader.ReadBytes(length);
            stream.Seek(pos, SeekOrigin.Begin);
            return bytes;
        }

        /// <summary>
        /// Begin writing a packet: the first 4 bytes indicate the size of the data that will follow.
        /// </summary>
        public BinaryWriter BeginPacket(byte packetID)
        {
            BinaryWriter writer = BeginWriting(false);
            writer.Write(0);
            writer.Write(packetID);
            return writer;
        }

        /// <summary>
        /// Begin writing a packet: the first 4 bytes indicate the size of the data that will follow.
        /// </summary>
        public BinaryWriter BeginPacket(Packet packet)
        {
            return BeginPacket((byte) packet);
        }

        /// <summary>
        /// Begin writing a packet: the first 4 bytes indicate the size of the data that will follow.
        /// </summary>
        public BinaryWriter BeginPacket(Packet packet, int startOffset)
        {
            BinaryWriter writer = BeginWriting(startOffset);
            writer.Write(0);
            writer.Write((byte) packet);
            return writer;
        }

        /// <summary>
        /// Finish writing of the packet, updating (and returning) its size.
        /// </summary>
        public int EndPacket()
        {
            if (isWriting)
            {
                size = Position;
                stream.Seek(0, SeekOrigin.Begin);
                binaryWriter.Write(size - SIZEOF_SIZE);
                stream.Seek(0, SeekOrigin.Begin);
                isWriting = false;
            }
            return size;
        }

        /// <summary>
        /// Finish writing of the packet, updating (and returning) its size.
        /// </summary>
        public int EndTcpPacketStartingAt(int startOffset)
        {
            if (isWriting)
            {
                size = Position;
                stream.Seek(startOffset, SeekOrigin.Begin);
                binaryWriter.Write(size - SIZEOF_SIZE - startOffset);
                stream.Seek(0, SeekOrigin.Begin);
                isWriting = false;
            }
            return size;
        }

        /// <summary>
        /// Finish writing the packet and reposition the stream's position to the specified offset.
        /// </summary>
        public int EndTcpPacketWithOffset(int offset)
        {
            if (isWriting)
            {
                size = Position;
                stream.Seek(0, SeekOrigin.Begin);
                binaryWriter.Write(size - SIZEOF_SIZE);
                stream.Seek(offset, SeekOrigin.Begin);
                isWriting = false;
            }
            return size;
        }

        public T Deserialize<T>(bool autoRecycle = true) where T : NetworkMessage, new()
        {
            T instance = new T();
            instance.Deserialize(this, autoRecycle);
            return instance;
        }
        
        #region Writer
        public void Write(long value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value);
            }
        }
        
        public void Write(long[] values)
        {
            if (isWriting)
            {
                binaryWriter.Write(values.Length);

                foreach (long value in values)
                {
                    binaryWriter.Write(value);
                }
            }
        }
        
        public void Write(ulong value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value);
            }
        }
        
        public void Write(ulong[] values)
        {
            if (isWriting)
            {
                binaryWriter.Write(values.Length);

                foreach (ulong value in values)
                {
                    binaryWriter.Write(value);
                }
            }
        }
        
        public void Write(int value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value);
            }
        }
        
        public void Write(int[] values)
        {
            if (isWriting)
            {
                binaryWriter.Write(values.Length);

                foreach (int value in values)
                {
                    binaryWriter.Write(value);
                }
            }
        }
        
        public void Write(uint value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value);
            }
        }
        
        public void Write(uint[] values)
        {
            if (isWriting)
            {
                binaryWriter.Write(values.Length);

                foreach (uint value in values)
                {
                    binaryWriter.Write(value);
                }
            }
        }
        
        public void Write(short value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value);
            }
        }
        
        public void Write(short[] values)
        {
            if (isWriting)
            {
                binaryWriter.Write(values.Length);

                foreach (short value in values)
                {
                    binaryWriter.Write(value);
                }
            }
        }
        
        public void Write(ushort value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value);
            }
        }
        
        public void Write(ushort[] values)
        {
            if (isWriting)
            {
                binaryWriter.Write(values.Length);

                foreach (ushort value in values)
                {
                    binaryWriter.Write(value);
                }
            }
        }
        
        public void Write(bool value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value);
            }
        }
        
        public void Write(byte value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value);
            }
        }
        
        public void Write(byte[] value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value);
            }
        }
        
        public void Write(sbyte value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value);
            }
        }
        
        public void Write(sbyte[] values)
        {
            if (isWriting)
            {
                binaryWriter.Write(values.Length);

                foreach (sbyte value in values)
                {
                    binaryWriter.Write(value);
                }
            }
        }
        
        public void Write(string value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value);
            }
        }
        
        public void Write(string[] values)
        {
            if (isWriting)
            {
                binaryWriter.Write(values.Length);

                foreach (string value in values)
                {
                    binaryWriter.Write(value);
                }
            }
        }
        
        public void Write(char value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value);
            }
        }
        
        public void Write(char[] values)
        {
            if (isWriting)
            {
                binaryWriter.Write(values.Length);

                foreach (char value in values)
                {
                    binaryWriter.Write(value);
                }
            }
        }
        
        public void Write(float value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value);
            }
        }
        
        public void Write(float[] values)
        {
            if (isWriting)
            {
                binaryWriter.Write(values.Length);

                foreach (float value in values)
                {
                    binaryWriter.Write(value);
                }
            }
        }
        
        public void Write(double value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value);
            }
        }
        
        public void Write(double[] values)
        {
            if (isWriting)
            {
                binaryWriter.Write(values.Length);

                foreach (double value in values)
                {
                    binaryWriter.Write(value);
                }
            }
        }
        
        public void Write(Vector2 value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value.x);
                binaryWriter.Write(value.y);
            }
        }
        
        public void Write(Vector2[] values)
        {
            if (isWriting)
            {
                binaryWriter.Write(values.Length);

                foreach (Vector2 value in values)
                {
                    Write(value);
                }
            }
        }
        
        public void Write(Vector3 value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value.x);
                binaryWriter.Write(value.y);
                binaryWriter.Write(value.z);
            }
        }
        
        public void Write(Vector3[] values)
        {
            if (isWriting)
            {
                binaryWriter.Write(values.Length);

                foreach (Vector3 value in values)
                {
                    Write(value);
                }
            }
        }
        
        public void Write(Quaternion value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value.x);
                binaryWriter.Write(value.y);
                binaryWriter.Write(value.z);
                binaryWriter.Write(value.w);
            }
        }
        
        public void Write(Quaternion[] values)
        {
            if (isWriting)
            {
                binaryWriter.Write(values.Length);

                foreach (Quaternion value in values)
                {
                    Write(value);
                }
            }
        }
        
        public void Write(Color value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value.r);
                binaryWriter.Write(value.g);
                binaryWriter.Write(value.b);
                binaryWriter.Write(value.a);
            }
        }
        
        public void Write(Color[] values)
        {
            if (isWriting)
            {
                binaryWriter.Write(values.Length);

                foreach (Color value in values)
                {
                    Write(value);
                }
            }
        }
        
        public void Write(Color32 value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value.r);
                binaryWriter.Write(value.g);
                binaryWriter.Write(value.b);
                binaryWriter.Write(value.a);
            }
        }
        
        public void Write(Color32[] values)
        {
            if (isWriting)
            {
                binaryWriter.Write(values.Length);

                foreach (Color value in values)
                {
                    Write(value);
                }
            }
        }
        
        public void Write(Rect value)
        {
            if (isWriting)
            {
                binaryWriter.Write(value.x);
                binaryWriter.Write(value.y);
                binaryWriter.Write(value.width);
                binaryWriter.Write(value.height);
            }
        }
        
        public void Write(Rect[] values)
        {
            if (isWriting)
            {
                binaryWriter.Write(values.Length);

                foreach (Rect value in values)
                {
                    Write(value);
                }
            }
        }

        public void Write(Object value)
        {
            switch (value)
            {
                case bool convertedValue : Write(convertedValue); break;
                case bool[] convertedValue : Write(convertedValue); break;
                case byte convertedValue : Write(convertedValue); break;
                case byte[] convertedValue : Write(convertedValue); break;
                case sbyte convertedValue : Write(convertedValue); break;
                case sbyte[] convertedValue : Write(convertedValue); break;
                case short convertedValue : Write(convertedValue); break;
                case short[] convertedValue : Write(convertedValue); break;
                case ushort convertedValue : Write(convertedValue); break;
                case ushort[] convertedValue : Write(convertedValue); break;
                case int convertedValue : Write(convertedValue); break;
                case int[] convertedValue : Write(convertedValue); break;
                case uint convertedValue : Write(convertedValue); break;
                case uint[] convertedValue : Write(convertedValue); break;
                case long convertedValue : Write(convertedValue); break;
                case long[] convertedValue : Write(convertedValue); break;
                case ulong convertedValue : Write(convertedValue); break;
                case ulong[] convertedValue : Write(convertedValue); break;
                case float convertedValue : Write(convertedValue); break;
                case float[] convertedValue : Write(convertedValue); break;
                case double convertedValue : Write(convertedValue); break;
                case double[] convertedValue : Write(convertedValue); break;
                case char convertedValue : Write(convertedValue); break;
                case char[] convertedValue : Write(convertedValue); break;
                case string convertedValue : Write(convertedValue); break;
                case string[] convertedValue : Write(convertedValue); break;
                case Vector2 convertedValue : Write(convertedValue); break;
                case Vector2[] convertedValue : Write(convertedValue); break;
                case Vector3 convertedValue : Write(convertedValue); break;
                case Vector3[] convertedValue : Write(convertedValue); break;
                case Quaternion convertedValue : Write(convertedValue); break;
                case Quaternion[] convertedValue : Write(convertedValue); break;
                case Color convertedValue : Write(convertedValue); break;
                case Color[] convertedValue : Write(convertedValue); break;
                case Color32 convertedValue : Write(convertedValue); break;
                case Color32[] convertedValue : Write(convertedValue); break;
                case Rect convertedValue : Write(convertedValue); break;
                case Rect[] convertedValue : Write(convertedValue); break;
            }
        }
        
        #endregion

        #region Reader

        public long ReadInt64()
        {
            return binaryReader.ReadInt64();
        }
        
        public long[] ReadInt64Array()
        {
            int length = binaryReader.ReadInt32();
            long[] result = new long[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = binaryReader.ReadInt64();
            }
            return result;
        }
        
        public ulong ReadUInt64()
        {
            return binaryReader.ReadUInt64();
        }
        
        public ulong[] ReadUInt64Array()
        {
            int length = binaryReader.ReadInt32();
            ulong[] result = new ulong[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = binaryReader.ReadUInt64();
            }
            return result;
        }
        
        public int ReadInt32()
        {
            return binaryReader.ReadInt32();
        }
        
        public int[] ReadInt32Array()
        {
            int length = binaryReader.ReadInt32();
            int[] result = new int[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = binaryReader.ReadInt32();
            }
            return result;
        }
        
        public uint ReadUInt32()
        {
            return binaryReader.ReadUInt32();
        }
        
        public uint[] ReadUInt32Array()
        {
            int length = binaryReader.ReadInt32();
            uint[] result = new uint[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = binaryReader.ReadUInt32();
            }
            return result;
        }
        
        public short ReadInt16()
        {
            return binaryReader.ReadInt16();
        }
        
        public short[] ReadInt16Array()
        {
            int length = binaryReader.ReadInt32();
            short[] result = new short[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = binaryReader.ReadInt16();
            }
            return result;
        }
        
        public ushort ReadUInt16()
        {
            return binaryReader.ReadUInt16();
        }
        
        public bool ReadBoolean()
        {
            return binaryReader.ReadBoolean();
        }
        
        public byte ReadByte()
        {
            return binaryReader.ReadByte();
        }
        
        public byte[] ReadBytes(int count)
        {
            return binaryReader.ReadBytes(count);
        }
        
        public sbyte ReadSByte()
        {
            return binaryReader.ReadSByte();
        }
        
        public sbyte[] ReadSBytes()
        {
            int length = binaryReader.ReadInt32();
            sbyte[] result = new sbyte[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = binaryReader.ReadSByte();
            }
            return result;
        }
        
        public string ReadString()
        {
            return binaryReader.ReadString();
        }
        
        public string[] ReadStrings()
        {
            int length = binaryReader.ReadInt32();
            string[] result = new string[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = binaryReader.ReadString();
            }
            return result;
        }
        
        public char ReadChar()
        {
            return binaryReader.ReadChar();
        }
        
        public char[] ReadChars()
        {
            int length = binaryReader.ReadInt32();
            char[] result = new char[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = binaryReader.ReadChar();
            }
            return result;
        }
        
        public float ReadSingle()
        {
            return binaryReader.ReadSingle();
        }
        
        public float[] ReadSingles()
        {
            int length = binaryReader.ReadInt32();
            float[] result = new float[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = binaryReader.ReadSingle();
            }
            return result;
        }
        
        public double ReadDouble()
        {
            return binaryReader.ReadDouble();
        }
        
        public double[] ReadDoubles()
        {
            int length = binaryReader.ReadInt32();
            double[] result = new double[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = binaryReader.ReadDouble();
            }
            return result;
        }
        
        public Vector2 ReadVector2()
        {
            return new Vector2(binaryReader.ReadSingle(), binaryReader.ReadSingle());
        }
        
        public Vector2[] ReadVector2Array()
        {
            int length = binaryReader.ReadInt32();
            Vector2[] result = new Vector2[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = ReadVector2();
            }
            return result;
        }
        
        public Vector3 ReadVector3()
        {
            return new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
        }
        
        public Vector3[] ReadVector3Array()
        {
            int length = binaryReader.ReadInt32();
            Vector3[] result = new Vector3[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = ReadVector3();
            }
            return result;
        }
        
        public Quaternion ReadQuaternion()
        {
            return new Quaternion(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
        }
        
        public Quaternion[] ReadQuaternions()
        {
            int length = binaryReader.ReadInt32();
            Quaternion[] result = new Quaternion[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = ReadQuaternion();
            }
            return result;
        }
        
        public Color ReadColor()
        {
            return new Color(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
        }
        
        public Color[] ReadColors()
        {
            int length = binaryReader.ReadInt32();
            Color[] result = new Color[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = ReadColor();
            }
            return result;
        }
        
        public Color32 ReadColor32()
        {
            return new Color32(binaryReader.ReadByte(), binaryReader.ReadByte(), binaryReader.ReadByte(), binaryReader.ReadByte());
        }
        
        public Color32[] ReadColor32Array()
        {
            int length = binaryReader.ReadInt32();
            Color32[] result = new Color32[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = ReadColor32();
            }
            return result;
        }
        
        public Rect ReadRect()
        {
            return new Rect(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
        }
        
        public Rect[] ReadRects()
        {
            int length = binaryReader.ReadInt32();
            Rect[] result = new Rect[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = ReadRect();
            }
            return result;
        }

        #endregion
    }
}