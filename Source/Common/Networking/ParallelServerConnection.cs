namespace Multiplayer.Common;

public class ParallelServerConnection(LiteNetConnection connection, ParallelReceiver receiver)
{
    public LiteNetConnection connection = connection;
    public ParallelReceiver receiver = receiver;
}
