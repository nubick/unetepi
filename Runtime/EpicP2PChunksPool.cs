using System;
using System.Collections.Generic;

namespace Unity.Netcode.Epic.Transport
{
    public class EpicP2PChunksPool
    {
        private readonly SendChunksPool _sendChunksPool = new();
        private readonly ReceiveChunksPool _receiveChunksPool = new();
        
        public byte[] GetReceiveChunk(uint size)
        {
            return _receiveChunksPool.Get(size);
        }
        
        public void ReleaseReceiveChunk(byte[] chunk)
        {
            _receiveChunksPool.Release(chunk);
        }
        
        public byte[] GetSendChunk(int size)
        {
            return _sendChunksPool.GetChunk(size);
        }
        
        public List<byte[]> GetSendChunksList(ArraySegment<byte> data)
        {
            return _sendChunksPool.GetChunksList(data);
        }

        private class ReceiveChunksPool
        {
            private readonly Dictionary<uint, Stack<byte[]>> _map = new();

            public byte[] Get(uint size)
            {
                Stack<byte[]> stack = GetStack(size);
                return stack.Count == 0 ? new byte[size] : stack.Pop();
            }

            public void Release(byte[] chunk)
            {
                Stack<byte[]> stack = GetStack((uint)chunk.Length);
                stack.Push(chunk);
            }

            private Stack<byte[]> GetStack(uint size)
            {
                if (!_map.ContainsKey(size))
                    _map.Add(size, new Stack<byte[]>());
                return _map[size];
            }
        }

        private class SendChunksPool
        {
            private const int MaxChunksAmount = 255;
            private int HeaderSize => EpicP2PTransport.HeaderSize;
            private int MaxPacketSize => (int)EpicP2PTransport.MaxPacketSize;
            
            private readonly byte[][] _fullSizeSendChunks;
            private readonly Dictionary<int, byte[]> _sendChunks;
            private readonly List<byte[]> _sendChunksListCache = new();
            
            public SendChunksPool()
            {
                _fullSizeSendChunks = new byte[MaxChunksAmount][]; //pool size: 255 * 1170 = 291kb
                for (int i = 0; i < MaxChunksAmount; i++)
                    _fullSizeSendChunks[i] = new byte[MaxPacketSize];

                _sendChunks = new Dictionary<int, byte[]>(); //pool size: 1 + 2 + 3 + ... + 1170 = 668kb
                for (int size = 1; size <= MaxPacketSize; size++)
                    _sendChunks.Add(size, new byte[size]);
            }
            
            public byte[] GetChunk(int size)
            {
                return _sendChunks[size];
            }

            public List<byte[]> GetChunksList(ArraySegment<byte> data)
            {
                if (data.Count <= MaxPacketSize)
                    throw new ArgumentOutOfRangeException($"Split doens't make sense as data size ({data.Count}) is less than chunk size ({MaxPacketSize})");

                int chunksAmount = GetChunksAmount(data.Count);

                if (chunksAmount > MaxChunksAmount)
                    throw new ArgumentOutOfRangeException($"Maximum supported chunks amount should be not greater than 255. Current amount: {chunksAmount}");

                _sendChunksListCache.Clear();

                byte[] firstChunk = _fullSizeSendChunks[0]; //first chunk is always full
                int nextChunkIndex = 1;
                firstChunk[0] = (byte)chunksAmount; //header
                Array.Copy(data.Array, 0, firstChunk, HeaderSize, MaxPacketSize - HeaderSize);
                _sendChunksListCache.Add(firstChunk);

                int offset = MaxPacketSize - HeaderSize;
                int remaining = data.Count + HeaderSize - MaxPacketSize;
                while (remaining > 0)
                {
                    int chunkSize = remaining > MaxPacketSize ? MaxPacketSize : remaining;
                    byte[] chunk = chunkSize == MaxPacketSize ? _fullSizeSendChunks[nextChunkIndex] : GetChunk(chunkSize);
                    nextChunkIndex++;
                    Array.Copy(data.Array, offset, chunk, 0, chunkSize);
                    _sendChunksListCache.Add(chunk);
                    remaining -= chunkSize;
                    offset += chunkSize;
                }

                return _sendChunksListCache;
            }

            private int GetChunksAmount(int dataCount)
            {
                int fullSize = dataCount + HeaderSize;
                int fullChunksAmount = fullSize / MaxPacketSize;
                int remaindersAmount = fullSize % MaxPacketSize == 0 ? 0 : 1;
                return fullChunksAmount + remaindersAmount;
            }
        }
    }
}