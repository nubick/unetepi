using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using UnityEngine;

namespace Unity.Netcode.Epic.Transport
{
    public class EpicP2PTransport : NetworkTransport
    {
        public static readonly uint MaxPacketSize = 1170;
        public static readonly int HeaderSize = 1;
        
        private EpicP2PSettings _settings;
        private EpicP2PEvents _events;
        private NetworkManager _networkManager;
        private EpicP2PChunksPool _chunksPool;

        private readonly EpicP2PUsers _users = new();
        private bool _isHostOrServer;
        private ReceivePacketOptions _receivePacketOptions;
        private readonly ReceivePacketResult _receivePacketResult = new();
        private readonly ArraySegment<byte> _payload = new(new byte[MaxPacketSize]);
        
        
        private LogLevel LogLevel => _networkManager.LogLevel;
        
        public override ulong ServerClientId => 0;

        public void Setup(EpicP2PSettings settings) => _settings = settings;
        
        public override void Initialize(NetworkManager networkManager = null)
        {
            _networkManager = networkManager;

            LogDeveloper($"Initialize, ServerProductUserId: {_settings.ServerProductUserId}, LocalProductUserId: {_settings.LocalProductUserId}");

            _users.Clear();
            _users.SetServerUser(_settings.ServerProductUserId);
            
            _events = new EpicP2PEvents(_settings, this);
            _chunksPool = new EpicP2PChunksPool();
            _isHostOrServer = false;
            
            _receivePacketOptions = new ReceivePacketOptions();
            _receivePacketOptions.LocalUserId = _settings.LocalProductUserId;
            _receivePacketOptions.MaxDataSizeBytes = MaxPacketSize;
            _receivePacketOptions.RequestedChannel = null; //All channels
            
            ClearPacketQueve();
        }

        public override bool StartServer()
        {
            LogDeveloper("Start Server");
            _isHostOrServer = true;
            return true;
        }

        public override bool StartClient()
        {
            LogDeveloper("Start Client");
            int hashCode = Mathf.Abs(_settings.LocalProductUserId.ToString().GetHashCode()); //SocketName can't have '-' sign.
            SocketId socketId = new SocketId { SocketName = $"{hashCode}{_settings.SessionId}" };
            _users.ServerUser.SetSocketId(socketId);
            SendConnectMessage(_users.ServerUser);
            return true;
        }

        public void OnPeerConnectionRequest(SocketId socketId, ProductUserId localUserId, ProductUserId remoteUserId)
        {
            if (_isHostOrServer)
            {
                bool isCorrectSession = socketId.SocketName.EndsWith(_settings.SessionId);
                if (isCorrectSession)
                {
                    LogDeveloper($"Accept connection from user '{remoteUserId}'");
                    AcceptConnectionOptions options = new AcceptConnectionOptions();
                    options.SocketId = socketId;
                    options.LocalUserId = localUserId;
                    options.RemoteUserId = remoteUserId;
                    Result result = _settings.P2PInterface.AcceptConnection(ref options);
                    LogResult(result, "AcceptConnection");

                    EpicP2PUser clientUser = _users.AddClientUser(remoteUserId, socketId);
                    SendConnectMessage(clientUser);
                }
                else
                {
                    LogDeveloper($"Wrong session: '{socketId.SocketName}'. Don't accept connection from user '{remoteUserId}'.");
                }
            }
            else
            {
                LogError($"Client got OnPeerConnectionRequest, socketId: {socketId.SocketName}, localUserId: {localUserId}, remoteUserId: {remoteUserId}");
            }
        }

        public void OnPeerConnectionClosed(ProductUserId remoteUserId)
        {
            EpicP2PUser user = _users.Get(remoteUserId);
            if (user == null)
            {
                LogDeveloper($"Disconnection detected for not connected user: '{remoteUserId}'. Ignore");
            }
            else
            {
                _users.Disconnected.Add(user);
                LogDeveloper($"Disconnection detected for: '{user}'");
            }
        }

        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkDelivery networkDelivery)
        {
            EpicP2PUser remoteUser = _users.Get(clientId);
            if (data.Count > MaxPacketSize)
            {
                SendChunks(remoteUser, data);
            }
            else
            {
                byte[] chunk = _chunksPool.GetSendChunk(data.Count);
                Array.Copy(data.Array, chunk, data.Count);
                SendSinglePacket(remoteUser, chunk, EpicP2PChannel.Default);
            }
        }

        private void SendConnectMessage(EpicP2PUser remoteUser)
        {
            SendSinglePacket(remoteUser, Array.Empty<byte>(), EpicP2PChannel.Connect);
        }

        private void SendSinglePacket(EpicP2PUser remoteUser, byte[] data, EpicP2PChannel channel)
        {
            LogDeveloper($"SendPacket, remoteUserId: {remoteUser}, size: {data.Length}, channel: {channel}");
            SendPacketOptions options = remoteUser.GetSendPacketOptions();
            options.Data = new ArraySegment<byte>(data);
            options.Channel = (byte)channel;
            options.LocalUserId = _settings.LocalProductUserId;
            Result result = _settings.P2PInterface.SendPacket(ref options);
            LogResult(result, "SendPacket");
        }

        private void SendChunks(EpicP2PUser remoteUser, ArraySegment<byte> data)
        {
            List<byte[]> chunks = _chunksPool.GetSendChunksList(data);
            LogDeveloper($"SendChunks, remoteUserId: {remoteUser}, amount: {chunks.Count}");
            foreach (byte[] chunk in chunks)
                SendSinglePacket(remoteUser, chunk, EpicP2PChannel.Chunks);
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            Result result = _settings.P2PInterface.ReceivePacket(ref _receivePacketOptions, out ProductUserId remoteUserId, out SocketId socketId, out byte outChannel, _payload, out uint outBytesWritten);

            _receivePacketResult.RemoteUserId = remoteUserId;
            _receivePacketResult.OutChannel = outChannel;
            _receivePacketResult.OutBytesWritten = outBytesWritten;

            HandleReceivedPacketResult(_receivePacketResult, result);

            receiveTime = Time.realtimeSinceStartup;
            clientId = _receivePacketResult.ClientId;
            payload = _receivePacketResult.Payload;

            return _receivePacketResult.NetworkEvent;
        }

        private void HandleReceivedPacketResult(ReceivePacketResult result, Result epicResult)
        {
            if (epicResult == Result.NotFound)
                HandleNotFound(result);
            else if (epicResult == Result.Success)
                HandleSuccess(result);
            else
                HandleNotSuccess(result, epicResult);
        }
        
        private void HandleNotFound(ReceivePacketResult result)
        {
            if (_users.Disconnected.Count > 0)
            {
                int lastIndex = _users.Disconnected.Count - 1;
                EpicP2PUser user = _users.Disconnected[lastIndex];
                _users.Disconnected.RemoveAt(lastIndex);
                
                if (_isHostOrServer)
                    _users.RemoveClientUser(user.ClientId);

                result.ClientId = user.ClientId;
                result.NetworkEvent = NetworkEvent.Disconnect;

                LogDeveloper($"PollEvent: User disconnect {user}");
            }
            else
            {
                result.ClientId = 0;
                result.NetworkEvent = NetworkEvent.Nothing;
            }
        }

        private void HandleNotSuccess(ReceivePacketResult result, Result epicResult)
        {
            result.ClientId = 0;
            result.NetworkEvent = NetworkEvent.Nothing;
            LogError($"PollEvent: Not success, result: {epicResult}");
        }

        private void HandleSuccess(ReceivePacketResult result)
        {
            EpicP2PUser remoteUser = _users.Get(result.RemoteUserId);
            if (remoteUser == null)
            {
                //pending packets from previous session or from user who was disconnected and removed
                result.ClientId = 0;
                result.NetworkEvent = NetworkEvent.Nothing;
                LogError("PollEvent: Success, remoteUser is null");
                return;
            }

            //for client it returns host always with clientId equal 0
            //for host it returns mapped client with generated clientId
            result.ClientId = remoteUser.ClientId;

            EpicP2PChannel channel = (EpicP2PChannel)result.OutChannel;
            switch (channel)
            {
                case EpicP2PChannel.Connect:
                {
                    result.NetworkEvent = NetworkEvent.Connect;
                    LogDeveloper($"PollEvent: Connect, clientId: {result.ClientId}");
                    break;
                }
                case EpicP2PChannel.Default:
                {
                    byte[] chunk = GetFilledReceiveChunk(_payload.Array, result.OutBytesWritten);
                    result.Payload = new ArraySegment<byte>(chunk);
                    result.NetworkEvent = NetworkEvent.Data;
                    _chunksPool.ReleaseReceiveChunk(chunk);
                    LogDeveloper($"PollEvent: Got Data: {_payload.Count}, outBytesWritten: {result.OutBytesWritten}, channel: {channel}, from: {result.RemoteUserId}");
                    break;
                }
                case EpicP2PChannel.Chunks:
                {
                    LogDeveloper($"PollEvent: Got chunk, size: {_payload.Count}, outBytesWritten: {result.OutBytesWritten}, channel: {channel}, from: {result.RemoteUserId}");
                    remoteUser.ReceivedChunks.Add(GetFilledReceiveChunk(_payload.Array, result.OutBytesWritten));
                    if (remoteUser.ReceivedChunks.IsFull)
                    {
                        byte[] fullData = remoteUser.ReceivedChunks.GetFullData();

                        foreach (byte[] chunk in remoteUser.ReceivedChunks.Chunks)
                            _chunksPool.ReleaseReceiveChunk(chunk);

                        remoteUser.ReceivedChunks.Release();

                        result.Payload = new ArraySegment<byte>(fullData);
                        result.NetworkEvent = NetworkEvent.Data;
                        LogDeveloper($"Got the latest chunk, full message size: {fullData.Length}");
                    }
                    else
                    {
                        result.Payload = null;
                        result.NetworkEvent = NetworkEvent.Nothing;
                    }
                    break;
                }
                default:
                    throw new Exception($"PollEvent: Not supported channel: {channel}");
            }
        }
        
        private byte[] GetFilledReceiveChunk(byte[] payloadArray, uint outBytesWritten)
        {
            byte[] chunk = _chunksPool.GetReceiveChunk(outBytesWritten);
            Array.Copy(payloadArray, chunk, outBytesWritten);
            return chunk;
        }
        
        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (!_isHostOrServer)
                throw new Exception("Only host or server can disconnect remote client");

            LogDeveloper($"DisconnectRemoteClient, clientId: {clientId}");

            EpicP2PUser clientUser = _users.Get(clientId);
            if (clientUser == null)
                LogError($"Can't find connected client by clientId: {clientId}");
            else
                CloseConnection(clientUser);
        }

        public override void DisconnectLocalClient()
        {
            LogDeveloper("DisconnectLocalClient");
            CloseAllConnections();
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            return 0;
        }
        
        public override void Shutdown()
        {
            LogDeveloper("Shutdown");
            CloseAllConnections();
            _isHostOrServer = false;
            _events.ClearSubscriptions();
            _users.Clear();
            _receivePacketOptions = new ReceivePacketOptions();
        }

        private void CloseAllConnections()
        {
            LogDeveloper("Close all opened connections");
            if (_isHostOrServer)
            {
                foreach (EpicP2PUser clientUser in _users.GetAllClients())
                    CloseConnection(clientUser);
            }
            else
            {
                CloseConnection(_users.ServerUser);
            }
        }

        private void CloseConnection(EpicP2PUser remoteUser)
        {
            LogDeveloper($"Close connection with remoteUser: '{remoteUser}'");
            CloseConnectionsOptions options = new CloseConnectionsOptions();
            options.LocalUserId = _settings.LocalProductUserId;
            options.SocketId = remoteUser.SocketId;
            Result result = _settings.P2PInterface.CloseConnections(ref options);
            LogResult(result, "CloseConnections");
        }

        private void ClearPacketQueve()
        {
            var options = new GetPacketQueueInfoOptions();
            _settings.P2PInterface.GetPacketQueueInfo(ref options, out PacketQueueInfo info);
            Debug.Log($"Incoming packets count: {info.IncomingPacketQueueCurrentPacketCount}, Incoming size bytes: {info.IncomingPacketQueueCurrentSizeBytes}, Outgoing packets count: {info.OutgoingPacketQueueCurrentPacketCount}, Outgoing size bytes: {info.OutgoingPacketQueueCurrentSizeBytes}");
            while (info.IncomingPacketQueueCurrentPacketCount > 0)
            {
                LogDeveloper($"Clear: ReceivePacket, remaining amount: {info.IncomingPacketQueueCurrentPacketCount}");
                ArraySegment<byte> data = new ArraySegment<byte>();
                _settings.P2PInterface.ReceivePacket(ref _receivePacketOptions, out ProductUserId remoteUserId, out SocketId socketId, out byte outChannel, data, out uint outBytesWritten);
                _settings.P2PInterface.GetPacketQueueInfo(ref options, out info);
            }
        }

        private class ReceivePacketResult
        {
            //In
            public ProductUserId RemoteUserId { get; set; }
            public byte OutChannel { get; set; }
            public uint OutBytesWritten { get; set; }

            //Out
            public ArraySegment<byte> Payload { get; set; }
            public NetworkEvent NetworkEvent { get; set; }
            public ulong ClientId { get; set; }
        }
        
        #region Logging...
        
        private void LogDeveloper(string message)
        {
            if (LogLevel <= LogLevel.Developer)
                NetworkLog.LogInfoServer($"[EpicP2PTransport]: {message}");
        }

        private void LogError(string message)
        {
            if (LogLevel <= LogLevel.Error)
                NetworkLog.LogErrorServer($"[EpicP2PTransport]: {message}");
        }

        private void LogResult(Result result, string operation)
        {
            if (LogLevel <= LogLevel.Developer && result != Result.Success)
                NetworkLog.LogWarningServer($"[EpicP2PTransport]: Result is not success but '{result}' for operation '{operation}'");
        }
        
        #endregion
    }
}