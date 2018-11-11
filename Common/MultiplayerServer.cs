﻿using LiteNetLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace Multiplayer.Common
{
    public class MultiplayerServer
    {
        static MultiplayerServer()
        {
            MpConnectionState.SetImplementation(ConnectionStateEnum.ServerSteam, typeof(ServerSteamState));
            MpConnectionState.SetImplementation(ConnectionStateEnum.ServerJoining, typeof(ServerJoiningState));
            MpConnectionState.SetImplementation(ConnectionStateEnum.ServerPlaying, typeof(ServerPlayingState));
        }

        public static MultiplayerServer instance;

        public const int DefaultPort = 30502;

        public int coopFactionId;
        public byte[] savedGame; // Compressed game save
        public Dictionary<int, byte[]> mapData = new Dictionary<int, byte[]>(); // Map id to compressed map data

        public Dictionary<int, List<byte[]>> mapCmds = new Dictionary<int, List<byte[]>>(); // Map id to serialized cmds list
        public Dictionary<int, List<byte[]>> tmpMapCmds;

        public Dictionary<string, int> playerFactions = new Dictionary<string, int>(); // Username to faction id

        public List<ServerPlayer> players = new List<ServerPlayer>();
        public IEnumerable<ServerPlayer> PlayingPlayers => players.Where(p => p.IsPlaying);

        public string hostUsername;
        public int timer;
        public ActionQueue queue = new ActionQueue();
        public IPAddress addr;
        public int port;
        public volatile bool running = true;
        public volatile bool allowLan;

        private Dictionary<string, ChatCmdHandler> chatCmds = new Dictionary<string, ChatCmdHandler>();

        public int keepAliveId;
        public Stopwatch lastKeepAlive = Stopwatch.StartNew();

        private NetManager server;

        public int nextUniqueId;

        public MultiplayerServer(IPAddress addr, int port = DefaultPort)
        {
            this.addr = addr;
            this.port = port;

            RegisterChatCmd("autosave", new ChatCmdAutosave());
            RegisterChatCmd("kick", new ChatCmdKick());

            StartNet();
        }

        private void StartNet()
        {
            EventBasedNetListener listener = new EventBasedNetListener();
            server = new NetManager(listener);

            listener.ConnectionRequestEvent += req => req.Accept();

            listener.PeerConnectedEvent += peer =>
            {
                IConnection conn = new MpNetConnection(peer);
                conn.State = ConnectionStateEnum.ServerJoining;
                peer.Tag = conn;
                OnConnected(conn);
            };

            listener.PeerDisconnectedEvent += (peer, info) =>
            {
                IConnection conn = peer.GetConnection();
                OnDisconnected(conn);
            };

            listener.NetworkLatencyUpdateEvent += (peer, ping) =>
            {
                peer.GetConnection().Latency = ping;
            };

            listener.NetworkReceiveEvent += (peer, reader, method) =>
            {
                byte[] data = reader.GetRemainingBytes();
                peer.GetConnection().serverPlayer.HandleReceive(data);
            };
        }

        public void StartListening()
        {
            server.Start(addr, IPAddress.IPv6Any, port);
        }

        public void Run()
        {
            Stopwatch time = Stopwatch.StartNew();
            double lag = 0;
            double timePerTick = 1000.0 / 60.0;

            while (running)
            {
                double elapsed = time.ElapsedMillisDouble();
                time.Restart();
                lag += elapsed;

                while (lag >= timePerTick)
                {
                    Tick();
                    lag -= timePerTick;
                }

                Thread.Sleep(10);
            }

            Stop();
        }

        private void Stop()
        {
            SendToAll(Packets.Server_DisconnectReason, new[] { "MpServerClosed" });
            foreach (var peer in server.GetPeers(ConnectionState.Connected))
                peer.Flush();

            server.Stop();

            instance = null;
        }

        public void Tick()
        {
            server.PollEvents();
            queue.RunQueue();

            if (timer % 3 == 0)
                SendToAll(Packets.Server_TimeControl, new object[] { timer });

            if (allowLan && timer % 60 == 0)
                server.SendDiscoveryRequest(Encoding.UTF8.GetBytes("mp-server"), 5100);

            timer++;

            if (timer % 180 == 0)
            {
                SendLatencies();

                keepAliveId++;
                SendToAll(Packets.Server_KeepAlive, new object[] { keepAliveId });
                lastKeepAlive.Restart();
            }
        }

        private void SendLatencies()
        {
            SendToAll(Packets.Server_PlayerList, new object[] { (byte)PlayerListAction.Latencies, PlayingPlayers.Select(p => p.Latency).ToArray() });
        }

        public void DoAutosave()
        {
            SendCommand(CommandType.Autosave, ScheduledCommand.NoFaction, ScheduledCommand.Global, new byte[0]);

            tmpMapCmds = new Dictionary<int, List<byte[]>>();
        }

        public void Enqueue(Action action)
        {
            queue.Enqueue(action);
        }

        private int nextPlayerId;

        public ServerPlayer OnConnected(IConnection conn)
        {
            if (conn.serverPlayer != null)
                MpLog.Error($"Connection {conn} already has a server player");

            conn.serverPlayer = new ServerPlayer(nextPlayerId++, conn);
            players.Add(conn.serverPlayer);
            MpLog.Log($"New connection: {conn}");

            return conn.serverPlayer;
        }

        public void OnDisconnected(IConnection conn)
        {
            if (conn.State == ConnectionStateEnum.Disconnected) return;

            ServerPlayer player = conn.serverPlayer;
            players.Remove(player);

            if (player.IsPlaying)
            {
                if (!players.Any(p => p.FactionId == player.FactionId))
                {
                    byte[] data = ByteWriter.GetBytes(player.FactionId);
                    SendCommand(CommandType.FactionOffline, ScheduledCommand.NoFaction, ScheduledCommand.Global, data);
                }

                SendNotification("MpPlayerDisconnected", conn.username);
                SendToAll(Packets.Server_PlayerList, new object[] { (byte)PlayerListAction.Remove, player.id });
            }

            conn.State = ConnectionStateEnum.Disconnected;

            MpLog.Log($"Disconnected: {conn}");
        }

        public void SendToAll(Packets id)
        {
            SendToAll(id, new byte[0]);
        }

        public void SendToAll(Packets id, object[] data)
        {
            SendToAll(id, ByteWriter.GetBytes(data));
        }

        public void SendToAll(Packets id, byte[] data)
        {
            foreach (ServerPlayer player in PlayingPlayers)
                player.conn.Send(id, data);
        }

        public ServerPlayer FindPlayer(Predicate<ServerPlayer> match)
        {
            lock (players)
            {
                return players.Find(match);
            }
        }

        public ServerPlayer GetPlayer(string username)
        {
            return FindPlayer(player => player.Username == username);
        }

        public IdBlock NextIdBlock(int blockSize = 30000)
        {
            int blockStart = nextUniqueId;
            nextUniqueId = nextUniqueId + blockSize;
            MpLog.Log($"New id block {blockStart} of size {blockSize}");

            return new IdBlock(blockStart, blockSize);
        }

        public void SendCommand(CommandType cmd, int factionId, int mapId, byte[] data, string sourcePlayer = null)
        {
            byte[] toSave = new ScheduledCommand(cmd, timer, factionId, mapId, data).Serialize();

            // todo cull target players if not global
            mapCmds.GetOrAddNew(mapId).Add(toSave);
            tmpMapCmds?.GetOrAddNew(mapId).Add(toSave);

            byte[] toSend = toSave.Append(new byte[] { 0 });
            byte[] toSendSource = toSave.Append(new byte[] { 1 });

            foreach (ServerPlayer player in PlayingPlayers)
            {
                player.conn.Send(
                    Packets.Server_Command,
                    sourcePlayer == player.Username ? toSendSource : toSend
                );
            }
        }

        public void SendNotification(string text, params string[] keys)
        {
            SendToAll(Packets.Server_Notification, new object[] { text, keys });
        }

        public void RegisterChatCmd(string cmdName, ChatCmdHandler handler)
        {
            chatCmds[cmdName] = handler;
        }

        public ChatCmdHandler GetCmdHandler(string cmdName)
        {
            chatCmds.TryGetValue(cmdName, out ChatCmdHandler handler);
            return handler;
        }
    }

    public class ServerPlayer
    {
        public int id;
        public IConnection conn;

        public string Username => conn.username;
        public int Latency => conn.Latency;
        public int FactionId => MultiplayerServer.instance.playerFactions[Username];
        public bool IsPlaying => conn.State == ConnectionStateEnum.ServerPlaying;
        public bool IsHost => MultiplayerServer.instance.hostUsername == Username;

        public MultiplayerServer Server => MultiplayerServer.instance;

        public ServerPlayer(int id, IConnection connection)
        {
            this.id = id;
            conn = connection;
        }

        public void HandleReceive(byte[] data)
        {
            try
            {
                conn.HandleReceive(data);
            }
            catch (Exception e)
            {
                MpLog.Error($"Error handling packet by {conn}: {e}");
                Disconnect($"Connection error: {e.GetType().Name}");
            }
        }

        public void Disconnect(string reason)
        {
            conn.Send(Packets.Server_DisconnectReason, reason);

            if (conn is MpNetConnection netConn)
                netConn.peer.Flush();

            conn.Close();
            Server.OnDisconnected(conn);
        }

        public void SendChat(string msg)
        {
            SendPacket(Packets.Server_Chat, new[] { msg });
        }

        public void SendPacket(Packets packet, object[] data)
        {
            conn.Send(packet, data);
        }

        public void SendPlayerList()
        {
            var writer = new ByteWriter();

            writer.WriteByte((byte)PlayerListAction.List);
            writer.WriteInt32(Server.PlayingPlayers.Count());

            foreach (var player in Server.PlayingPlayers)
            {
                writer.WriteInt32(player.id);
                writer.WriteString(player.Username);
                writer.WriteInt32(player.Latency);
            }

            conn.Send(Packets.Server_PlayerList, writer.GetArray());
        }
    }

    public class IdBlock
    {
        public int blockStart;
        public int blockSize;
        public int mapId = -1;

        public int current;
        public bool overflowHandled;

        public IdBlock(int blockStart, int blockSize, int mapId = -1)
        {
            this.blockStart = blockStart;
            this.blockSize = blockSize;
            this.mapId = mapId;
        }

        public int NextId()
        {
            // Overflows should be handled by the caller
            current++;
            return blockStart + current;
        }

        public byte[] Serialize()
        {
            ByteWriter writer = new ByteWriter();
            writer.WriteInt32(blockStart);
            writer.WriteInt32(blockSize);
            writer.WriteInt32(mapId);
            writer.WriteInt32(current);

            return writer.GetArray();
        }

        public static IdBlock Deserialize(ByteReader data)
        {
            IdBlock block = new IdBlock(data.ReadInt32(), data.ReadInt32(), data.ReadInt32());
            block.current = data.ReadInt32();
            return block;
        }
    }

    public class ActionQueue
    {
        private Queue<Action> queue = new Queue<Action>();
        private Queue<Action> tempQueue = new Queue<Action>();

        public void RunQueue()
        {
            lock (queue)
            {
                if (queue.Count > 0)
                {
                    foreach (Action a in queue)
                        tempQueue.Enqueue(a);
                    queue.Clear();
                }
            }

            try
            {
                while (tempQueue.Count > 0)
                    tempQueue.Dequeue().Invoke();
            }
            catch (Exception e)
            {
                MpLog.LogLines("Exception while executing action queue", e.ToString());
            }
        }

        public void Enqueue(Action action)
        {
            lock (queue)
                queue.Enqueue(action);
        }
    }
}
