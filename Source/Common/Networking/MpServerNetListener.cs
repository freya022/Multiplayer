using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using Verse;

namespace Multiplayer.Common
{
    public class MpServerNetListener(MultiplayerServer server, bool arbiter) : INetEventListener
    {
        private Dictionary<IPAddress, ParallelServerConnection> connections = new();

        public void OnConnectionRequest(ConnectionRequest req)
        {
            var result = server.playerManager.OnPreConnect(req.RemoteEndPoint.Address);
            if (result != null)
            {
                ServerLog.Log($"Connection request from {req.RemoteEndPoint} denied because {result.Value}");
                req.Reject(ConnectionBase.GetDisconnectBytes(result.Value));
                return;
            }
            ServerLog.Log($"Connection request from {req.RemoteEndPoint} accepted.");
            req.Accept();
        }

        public void OnPeerConnected(NetPeer peer)
        {
            lock (connections)
            {
                bool present = connections.TryGetValue(peer.EndPoint.Address, out ParallelServerConnection connectionInfo);
                if (!present || (present && (connectionInfo?.connection.State == ConnectionStateEnum.Disconnected || connectionInfo?.connection.defaultPeer.Tag == null)))
                {
                    connectionInfo = new ParallelServerConnection(new LiteNetConnection(peer), new ParallelReceiver());
                    connections.SetOrAdd(peer.EndPoint.Address, connectionInfo);
                    ConnectionBase conn = connectionInfo.connection;
                    conn.ChangeState(ConnectionStateEnum.ServerJoining);
                    peer.Tag = conn;
                    var player = server.playerManager.OnConnected(conn);
                    if (arbiter)
                    {
                        player.type = PlayerType.Arbiter;
                        player.color = new ColorRGB(128, 128, 128);
                    }
                }
                else if (connectionInfo?.connection != null)
                {
                    connectionInfo.connection.AddPeer(peer);
                    ConnectionBase conn = connectionInfo.connection;
                    peer.Tag = conn;
                }
            }
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            ServerLog.Log($"Peer Disconnected {peer.EndPoint} with {disconnectInfo.Reason} and {disconnectInfo.SocketErrorCode}");
            ConnectionBase conn = peer.GetConnection();
            server.playerManager.SetDisconnected(conn, MpDisconnectReason.ClientLeft);
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
            peer.GetConnection().Latency = latency;
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod method)
        {
            byte[] data = reader.GetRemainingBytes();
            lock (connections)
            {
                if (connections.TryGetValue(peer.EndPoint.Address, out ParallelServerConnection connectionInfo))
                {
                    ServerPlayer? player = connectionInfo.connection.serverPlayer;
                    if (peer == connectionInfo.connection.defaultPeer)
                    {
                        player?.HandleReceive(new ByteReader(data), method == DeliveryMethod.ReliableOrdered);
                    }
                    else
                    {
                        var result = connectionInfo.receiver.OnParallelNetworkReceive(data);
                        if (result.passAlong)
                        {
                            player?.HandleReceive(result.reader, method == DeliveryMethod.ReliableOrdered);
                        }
                    }
                }
            }
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            ServerLog.Log($"Network error from peer {endPoint} with {socketError}");
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    }
}
