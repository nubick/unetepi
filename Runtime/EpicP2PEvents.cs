using Epic.OnlineServices.P2P;
using UnityEngine;

namespace Unity.Netcode.Epic.Transport
{
    public class EpicP2PEvents
    {
        private readonly EpicP2PSettings _settings;
        private readonly EpicP2PTransport _transport;
        private ulong _connectionRequestId;
        private ulong _connectionClosedId;
        private ulong _queueFullId;

        public EpicP2PEvents(EpicP2PSettings settings, EpicP2PTransport transport)
        {
            _settings = settings;
            _transport = transport;
            Subscribe();
        }

        private void Subscribe()
        {
            AddNotifyPeerConnectionRequestOptions connectionRequestOptions = new AddNotifyPeerConnectionRequestOptions();
            connectionRequestOptions.LocalUserId = _settings.LocalProductUserId;
            connectionRequestOptions.SocketId = null;
            _connectionRequestId = _settings.P2PInterface.AddNotifyPeerConnectionRequest(ref connectionRequestOptions, null, OnPeerConnectionRequest);

            AddNotifyPeerConnectionClosedOptions connectionClosedOptions = new AddNotifyPeerConnectionClosedOptions();
            connectionClosedOptions.LocalUserId = _settings.LocalProductUserId;
            connectionClosedOptions.SocketId = null;
            _connectionClosedId = _settings.P2PInterface.AddNotifyPeerConnectionClosed(ref connectionClosedOptions, null, OnPeerConnectionClosed);

            AddNotifyIncomingPacketQueueFullOptions queueFullOptions = new AddNotifyIncomingPacketQueueFullOptions();
            _queueFullId = _settings.P2PInterface.AddNotifyIncomingPacketQueueFull(ref queueFullOptions, null, OnIncomingPacketQueueFull);
        }

        private void OnPeerConnectionRequest(ref OnIncomingConnectionRequestInfo info)
        {
            Debug.Log($"OnPeerConnectionRequest: socket: {info.SocketId.Value.SocketName}, localUserId: {info.LocalUserId}, remoteUserId: {info.RemoteUserId}");
            _transport.OnPeerConnectionRequest(info.SocketId.Value, info.LocalUserId, info.RemoteUserId);
        }

        private void OnPeerConnectionClosed(ref OnRemoteConnectionClosedInfo info)
        {
            Debug.Log($"OnPeerConnectionClosed: reason: {info.Reason}, socket: {info.SocketId.Value.SocketName}, localUserId: {info.LocalUserId}, remoteUserId: {info.RemoteUserId}");
            _transport.OnPeerConnectionClosed(info.RemoteUserId);
        }

        private void OnIncomingPacketQueueFull(ref OnIncomingPacketQueueFullInfo info)
        {
            Debug.Log($"OnIncomingPacketQueueFull: userId: {info.OverflowPacketLocalUserId}, channel: {info.OverflowPacketChannel}, currentSize: {info.PacketQueueCurrentSizeBytes}, maxSize: {info.PacketQueueMaxSizeBytes}, size: {info.OverflowPacketSizeBytes}");
        }

        public void ClearSubscriptions()
        {
            _settings.P2PInterface.RemoveNotifyPeerConnectionRequest(_connectionRequestId);
            _connectionRequestId = 0;
            
            _settings.P2PInterface.RemoveNotifyPeerConnectionRequest(_connectionClosedId);
            _connectionClosedId = 0;
            
            _settings.P2PInterface.RemoveNotifyPeerConnectionRequest(_queueFullId);
            _queueFullId = 0;
        }
    }
}