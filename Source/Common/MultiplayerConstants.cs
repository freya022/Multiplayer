namespace Multiplayer.Common;

public static class MultiplayerConstants
{
    // Cannot be more than 31 for the ParallelismFlag to work
    public const int ParallelTransferCount = 8;
    // Indicates the total quantity of ports to read from for big transfers + 1 for normal messages
    public const int Parallelism = ParallelTransferCount+1;
    public const int ParallelismFlag = 0b0000_0000_0000_0000_1111_1111;

}
