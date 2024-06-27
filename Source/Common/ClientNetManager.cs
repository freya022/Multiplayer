using LiteNetLib;

namespace Multiplayer.Common;

public class ClientNetManager
{
    public readonly NetManager[] netManagers = new NetManager[MultiplayerConstants.Parallelism];
    public NetStatistics Statistics => netManagers[0].Statistics;

    public void PollEvents()
    {
        foreach (NetManager netManager in netManagers)
        {
            netManager.PollEvents();
        }
    }
    public void Stop()
    {
        foreach (NetManager netManager in netManagers)
        {
            netManager.Stop();
        }
    }
}
