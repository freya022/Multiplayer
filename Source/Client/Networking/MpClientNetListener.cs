using System.Collections.Generic;
using LiteNetLib;
using Multiplayer.Common;
using System.Net;
using System.Net.Sockets;
using Multiplayer.Client.Util;
using Verse;

namespace Multiplayer.Client.Networking
{

    public class MpClientNetListener : INetEventListener
    {
        private LiteNetConnection connection;
        private NetPeer defaultPeer;
        // Transfer Index (Index)
        //  => Transfer Buffer
        //      => int transferFlag // binary boolean flag of currently transferred bytes
        //      => byte[][] buffer // buffer of bytes
        //          => 0 index // 0th peer buffer based on endpoint port
        //              => byte[]
        //          => 1 index // 1st peer buffer based on endpoint port
        //              => byte[]
        //          ...
        //          => n index // nth peer buffer based on endpoint port, where n = (parallelism - 2)
        //              => byte[]
        private readonly ParallelReceiver receiver = new();

        private void ReloadConnection(NetPeer peer)
        {
            //Log.Message($"Connecting default peer client: {peer.EndPoint}");
            connection = new LiteNetConnection(peer);
            defaultPeer = peer;
            ConnectionBase conn = connection;
            conn.username = Multiplayer.username;
            conn.ChangeState(ConnectionStateEnum.ClientJoining);
            Multiplayer.session.client = conn;
            Multiplayer.session.ReapplyPrefs();
            //Log.Message($"Updated default peer client: {peer.EndPoint}");
        }
        public void OnPeerConnected(NetPeer peer)
        {
            //Log.Message($"Connecting to peer: {peer.EndPoint}");
            if (connection == null)
            {
                ReloadConnection(peer);
            }
            else
            {
                //Log.Message($"Locking connection to peer: {peer.EndPoint}");
                lock (connection)
                {
                    if (connection.State == ConnectionStateEnum.Disconnected)
                    {
                        ReloadConnection(peer);
                    }
                    else
                    {
                        //Log.Message($"Adding peer to connection: {peer.EndPoint}");
                        connection.AddPeer(peer);
                    }
                }
            }
            //Log.Message($"Net client connected: {peer.EndPoint}");
            MpLog.Log("Net client connected");
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError error)
        {
            MpLog.Warn($"Net client error {error}");
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod method)
        {
            byte[] data = reader.GetRemainingBytes();
            if (peer == defaultPeer) // Default communication channel, ignore parallelism. Mostly for small transfers.
            {
                ClientUtil.HandleReceive(new ByteReader(data), method == DeliveryMethod.ReliableOrdered);
            }
            else
            {
                var result = receiver.OnParallelNetworkReceive(data);
                if (result.passAlong)
                {
                    ClientUtil.HandleReceive(result.reader, method == DeliveryMethod.ReliableOrdered);
                }
            }

        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            MpLog.Warn($"Peer {peer.EndPoint} disconnected because of {info.Reason} with {info.SocketErrorCode}");
            MpDisconnectReason reason;
            byte[] data;

            if (info.AdditionalData.IsNull)
            {
                if (info.Reason is DisconnectReason.DisconnectPeerCalled or DisconnectReason.RemoteConnectionClose)
                    reason = MpDisconnectReason.Generic;
                else if (Multiplayer.Client == null)
                    reason = MpDisconnectReason.ConnectingFailed;
                else
                    reason = MpDisconnectReason.NetFailed;

                data = new [] { (byte)info.Reason };
            }
            else
            {
                var reader = new ByteReader(info.AdditionalData.GetRemainingBytes());
                reason = (MpDisconnectReason)reader.ReadByte();
                data = reader.ReadPrefixedBytes();
            }

            Multiplayer.session.ProcessDisconnectPacket(reason, data);
            ConnectionStatusListeners.TryNotifyAll_Disconnected();

            Multiplayer.StopMultiplayer();
            MpLog.Log($"Net client disconnected {info.Reason}");
        }

        public void OnConnectionRequest(ConnectionRequest request)
        {
            Log.Message($"Connection Request: {request}");
        }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    }
}
