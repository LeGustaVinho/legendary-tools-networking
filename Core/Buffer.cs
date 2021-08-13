using System.Collections.Generic;
using System.IO;

namespace LegendaryTools.Networking
{
    /// <summary>
    /// This class merges BinaryWriter and BinaryReader into one.
    /// </summary>
    public class Buffer
    {
        private static ListLessGarb<Buffer> pool = new ListLessGarb<Buffer>();

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

        /// <summary>
        /// Peek at the first byte at the specified offset.
        /// </summary>
        public int PeekByte(int offset)
        {
            long pos = stream.Position;
            if (offset < 0 || offset + 1 > pos)
            {
                return -1;
            }
            stream.Seek(offset, SeekOrigin.Begin);
            int val = binaryReader.ReadByte();
            stream.Seek(pos, SeekOrigin.Begin);
            return val;
        }

        /// <summary>
        /// Peek at the first integer at the specified offset.
        /// </summary>
        public int PeekInt(int offset)
        {
            long pos = stream.Position;
            if (offset < 0 || offset + 4 > pos)
            {
                return -1;
            }
            stream.Seek(offset, SeekOrigin.Begin);
            int val = binaryReader.ReadInt32();
            stream.Seek(pos, SeekOrigin.Begin);
            return val;
        }

        /// <summary>
        /// Peek-read the specified number of bytes.
        /// </summary>
        public byte[] PeekBytes(int offset, int length)
        {
            long pos = stream.Position;
            if (offset < 0 || offset + length > pos)
            {
                return null;
            }
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
            BinaryWriter writer = BeginWriting(false);
            writer.Write(0);
            writer.Write((byte) packet);
            return writer;
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
                binaryWriter.Write(size - 4);
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
                binaryWriter.Write(size - 4 - startOffset);
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
                binaryWriter.Write(size - 4);
                stream.Seek(offset, SeekOrigin.Begin);
                isWriting = false;
            }
            return size;
        }
    }
}