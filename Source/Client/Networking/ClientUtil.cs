using LiteNetLib;
using Multiplayer.Common;
using Steamworks;
using System;
using Verse;
using Multiplayer.Client.Networking;

namespace Multiplayer.Client
{
    public static class ClientUtil
    {
        public static (NetPeer, ClientNetManager, MpClientNetListener) ConnectClient(string address, int port)
        {
            Multiplayer.session = new MultiplayerSession
            {
                address = address,
                port = port
            };
            NetPeer defaultPeer = null;
            MpClientNetListener clientNetListener = new MpClientNetListener();
            ClientNetManager netClient = new ClientNetManager();
            for (int i = 0; i < MultiplayerConstants.Parallelism; i++)
            {
                NetManager netSubClient = new NetManager(clientNetListener)
                {
                    EnableStatistics = true,
                    IPv6Enabled = MpUtil.SupportsIPv6() ? IPv6Mode.SeparateSocket : IPv6Mode.Disabled
                };
                netSubClient.Start();
                netSubClient.ReconnectDelay = 300;
                netSubClient.MaxConnectAttempts = 8;
                if (defaultPeer == null)
                {
                    defaultPeer = netSubClient.Connect(address, port, "");
                }
                else
                {
                    netSubClient.Connect(address, port+i, "");
                }
                netClient.netManagers[i] = netSubClient;
            }

            Multiplayer.session.netClient = netClient;
            return (defaultPeer, netClient, clientNetListener);
        }
        public static void TryConnectWithWindow(string address, int port, bool returnToServerBrowser = true)
        {
            Find.WindowStack.Add(new ConnectingWindow(address, port) { returnToServerBrowser = returnToServerBrowser });
            ConnectClient(address, port);
        }

        public static void TrySteamConnectWithWindow(CSteamID user, bool returnToServerBrowser = true)
        {
            Log.Message("Connecting through Steam");

            Multiplayer.session = new MultiplayerSession
            {
                client = new SteamClientConn(user) { username = Multiplayer.username },
                steamHost = user
            };

            Find.WindowStack.Add(new SteamConnectingWindow(user) { returnToServerBrowser = returnToServerBrowser });

            Multiplayer.session.ReapplyPrefs();
            Multiplayer.Client.ChangeState(ConnectionStateEnum.ClientSteam);
        }

        public static void HandleReceive(ByteReader data, bool reliable)
        {
            try
            {
                Multiplayer.Client.HandleReceiveRaw(data, reliable);
            }
            catch (Exception e)
            {
                Log.Error($"Exception handling packet by {Multiplayer.Client}: {e}");

                Multiplayer.session.disconnectInfo.titleTranslated = "MpPacketErrorLocal".Translate();

                ConnectionStatusListeners.TryNotifyAll_Disconnected();
                Multiplayer.StopMultiplayer();
            }
        }
    }

}
