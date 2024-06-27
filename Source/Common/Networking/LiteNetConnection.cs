using LiteNetLib;

namespace Multiplayer.Common
{
    public class LiteNetConnection : ConnectionBase
    {
        public readonly NetPeer defaultPeer;
        private readonly ParallelSender parallelSender = new();

        public LiteNetConnection(NetPeer peer)
        {
            defaultPeer = peer;
            parallel = true;
        }

        public void AddPeer(NetPeer peer)
        {
            parallelSender.AddPeer(peer);
            peer.Tag = this;
        }

        public void ClearParallelPeers()
        {
            parallelSender.Clear();
        }

        protected override void SendRaw(byte[] raw, bool reliable = true, bool shouldSendInParallel = false)
        {
            if (shouldSendInParallel && MultiplayerConstants.Parallelism > 1) // Prevent divide by zero error.
            {
                parallelSender.Send(raw, reliable);
            }
            else
            {
                defaultPeer.Send(raw, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
            }

        }



        public override void Close(MpDisconnectReason reason, byte[]? data = null)
        {
            ServerLog.Log($"Closing Connection because of {reason}");
            defaultPeer.NetManager.TriggerUpdate();
            defaultPeer.NetManager.DisconnectPeer(defaultPeer, GetDisconnectBytes(reason, data));
            defaultPeer.Tag = null;
            foreach (var peer in parallelSender.parallelPeers)
            {
                if (peer == null) continue;
                peer.NetManager.TriggerUpdate(); // todo: is this needed?
                peer.NetManager.DisconnectPeer(peer, GetDisconnectBytes(reason, data));
                peer.Tag = null;
            }
            ClearParallelPeers();
        }

        public override string ToString()
        {
            return $"NetConnection ({defaultPeer.EndPoint}) ({username})";
        }
    }
}
