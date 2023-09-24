using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;

namespace Unity.Netcode.Epic.Transport
{
    public class EpicP2PUser
    {
        public ProductUserId ProductUserId { get; }
        public ulong ClientId { get; }
        public SocketId SocketId { get; private set; }
        public EpicP2PReceivedChunks ReceivedChunks { get; } = new();
        
        public EpicP2PUser(ProductUserId productUserId, ulong clientId)
        {
            ProductUserId = productUserId;
            ClientId = clientId;
            ReceivedChunks = new EpicP2PReceivedChunks();
        }
        
        public void SetSocketId(SocketId socketId)
        {
            SocketId = socketId;
        }

        public SendPacketOptions GetSendPacketOptions()
        {
            SendPacketOptions options = new SendPacketOptions();
            options.RemoteUserId = ProductUserId;
            options.SocketId = SocketId;
            options.Reliability = PacketReliability.ReliableOrdered;
            options.AllowDelayedDelivery = true;
            return options;
        }

        public override string ToString() => $"[{ProductUserId}|{ClientId}|{SocketId.SocketName}]";
    }

    public class EpicP2PUsers
    {
        private ulong _nextClientId;
        private readonly Dictionary<ulong, EpicP2PUser> _clientIdUsersMap = new();
        private readonly Dictionary<ProductUserId, EpicP2PUser> _productIdUsersMap = new();
        private readonly Dictionary<ProductUserId, ulong> _productClientIdMap = new();
        
        public EpicP2PUser ServerUser { get; private set; }
        public List<EpicP2PUser> Disconnected { get; set; } = new();

        public void SetServerUser(ProductUserId serverUserId)
        {
            ServerUser = new EpicP2PUser(serverUserId, 0);
        }
        
        public EpicP2PUser AddClientUser(ProductUserId productUserId, SocketId socketId)
        {
            if (!_productClientIdMap.ContainsKey(productUserId))
            {
                _productClientIdMap.Add(productUserId, _nextClientId);
                _nextClientId++;
            }
            ulong clientId = _productClientIdMap[productUserId];
            EpicP2PUser clientUser = new EpicP2PUser(productUserId, clientId);
            clientUser.SetSocketId(socketId);
            _clientIdUsersMap.Add(clientUser.ClientId, clientUser);
            _productIdUsersMap.Add(productUserId, clientUser);
            return clientUser;
        }

        public void RemoveClientUser(ulong clientId)
        {
            EpicP2PUser clientUser = Get(clientId);
            _clientIdUsersMap.Remove(clientId);
            _productIdUsersMap.Remove(clientUser.ProductUserId);
        }

        public IReadOnlyCollection<EpicP2PUser> GetAllClients()
        {
            return new ReadOnlyCollection<EpicP2PUser>(_clientIdUsersMap.Values.ToList());
        }

        public EpicP2PUser Get(ulong clientId)
        {
            if (clientId == ServerUser.ClientId)
                return ServerUser;
            
            _clientIdUsersMap.TryGetValue(clientId, out EpicP2PUser user);
            return user;
        }

        public EpicP2PUser Get(ProductUserId userId)
        {
            if (userId == ServerUser.ProductUserId)
                return ServerUser;
            
            _productIdUsersMap.TryGetValue(userId, out EpicP2PUser user);
            return user;
        }

        public void Clear()
        {
            _nextClientId = 1;
            _productClientIdMap.Clear();
            ServerUser = null;
            _clientIdUsersMap.Clear();
            _productIdUsersMap.Clear();
            Disconnected.Clear();
        }
    }
}