using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.Netcode.Epic.Transport
{
    public class EpicP2PReceivedChunks
    {
        public List<byte[]> Chunks { get; } = new();
        private byte _amount;

        public bool IsEmpty => Chunks.Count == 0;
        public bool IsFull => Chunks.Count == _amount;
        
        public void Add(byte[] chunk)
        {
            if (IsEmpty)
                _amount = chunk[0];
            
            Chunks.Add(chunk);
        }
        
        public byte[] GetFullData()
        {
            int size = Chunks.Sum(bytes => bytes.Length) - EpicP2PTransport.HeaderSize;
            byte[] data = new byte[size];
            int sourceOffset = EpicP2PTransport.HeaderSize;
            int offset = 0;
            foreach (byte[] chunk in Chunks)
            {
                Array.Copy(chunk, sourceOffset, data, offset, chunk.Length - sourceOffset);
                offset += chunk.Length - sourceOffset;
                sourceOffset = 0;
            }
            return data;
        }
        
        public void Release()
        {
            _amount = 0;
            Chunks.Clear();
        }
    }
}