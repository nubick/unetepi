using Epic.OnlineServices;
using Epic.OnlineServices.P2P;

namespace Unity.Netcode.Epic.Transport
{
    public class EpicP2PSettings
    {
        public P2PInterface P2PInterface { get; private set; }
        public ProductUserId ServerProductUserId { get; private set; }
        public ProductUserId LocalProductUserId { get; private set; }
        public string SessionId { get; private set; }

        public EpicP2PSettings(P2PInterface p2PInterface, ProductUserId serverProductUserId, ProductUserId localProductUserId, string sessionId)
        {
            P2PInterface = p2PInterface;
            ServerProductUserId = serverProductUserId;
            LocalProductUserId = localProductUserId;
            SessionId = sessionId;
        }
    }
}