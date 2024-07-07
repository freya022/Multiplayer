﻿using System.Collections.Generic;
using System.Linq;
using Ionic.Zlib;
using Multiplayer.Client.Saving;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client;

public enum LoadingState
{
    Waiting,
    Downloading
}

public class ClientLoadingState : ClientBaseState
{
    public LoadingState subState = LoadingState.Waiting;

    public ClientLoadingState(ConnectionBase connection) : base(connection)
    {
    }

    [PacketHandler(Packets.Server_WorldDataStart)]
    public void HandleWorldDataStart(ByteReader data)
    {
        subState = LoadingState.Downloading;
        connection.Lenient = false; // Lenient is set while rejoining
    }

    [PacketHandler(Packets.Server_WorldData)]
    [IsFragmented]
    public void HandleWorldData(ByteReader data)
    {
        Log.Message("Game data size: " + data.Length);

        int factionId = data.ReadInt32();
        Multiplayer.session.myFactionId = factionId;

        int tickUntil = data.ReadInt32();
        int remoteSentCmds = data.ReadInt32();
        bool serverFrozen = data.ReadBool();

        byte[] worldData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
        byte[] sessionData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());

        var mapCmdsDict = new Dictionary<int, List<ScheduledCommand>>();
        var mapDataDict = new Dictionary<int, byte[]>();
        List<int> mapsToLoad = new List<int>();

        int mapCmdsCount = data.ReadInt32();
        for (int i = 0; i < mapCmdsCount; i++)
        {
            int mapId = data.ReadInt32();

            int mapCmdsLen = data.ReadInt32();
            List<ScheduledCommand> mapCmds = new List<ScheduledCommand>(mapCmdsLen);
            for (int j = 0; j < mapCmdsLen; j++)
                mapCmds.Add(ScheduledCommand.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

            mapCmdsDict[mapId] = mapCmds;
        }

        int mapDataCount = data.ReadInt32();
        for (int i = 0; i < mapDataCount; i++)
        {
            int mapId = data.ReadInt32();
            byte[] rawMapData = data.ReadPrefixedBytes();

            byte[] mapData = GZipStream.UncompressBuffer(rawMapData);
            mapDataDict[mapId] = mapData;
            mapsToLoad.Add(mapId);
        }

        Session.dataSnapshot = new GameDataSnapshot(
            0,
            worldData,
            sessionData,
            mapDataDict,
            mapCmdsDict
        );

        TickPatch.tickUntil = tickUntil;
        Multiplayer.session.receivedCmds = remoteSentCmds;
        Multiplayer.session.remoteTickUntil = tickUntil;
        TickPatch.serverFrozen = serverFrozen;

        int syncInfos = data.ReadInt32();
        for (int i = 0; i < syncInfos; i++)
            Session.initialOpinions.Add(ClientSyncOpinion.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

        Log.Message(syncInfos > 0
            ? $"Initial sync opinions: {Session.initialOpinions.First().startTick}...{Session.initialOpinions.Last().startTick}"
            : "No initial sync opinions");

        TickPatch.SetSimulation(
            toTickUntil: true,
            onFinish: () => Multiplayer.Client.Send(Packets.Client_WorldReady),
            cancelButtonKey: "Quit",
            onCancel: GenScene.GoToMainMenu // Calls StopMultiplayer through a patch
        );

        Loader.ReloadGame(mapsToLoad, true, false);
        connection.Send(Packets.Client_Playing);
        connection.ChangeState(ConnectionStateEnum.ClientPlaying);
    }
}
