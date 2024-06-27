using System;
using System.Text;
using Verse;

namespace Multiplayer.Common;

public class ParallelTransferBuffer
{
    private const int BufferSize = MultiplayerConstants.Parallelism - 1;
    public int transferFlag;
    private int byteCount;
    public byte[][] transferBytes;

    public ParallelTransferBuffer(int index, byte[] data)
    {
        transferBytes = new byte[BufferSize][];
        for (int i = 0; i < BufferSize; i++)
        {
            transferBytes[i] = [];
        }

        AddToBuffer(index, data);
    }


    // Example (using a byte for simplicity)
    // parallelConnectionIndex = 3
    // transferFlag = 0b0000_1011
    // Then you have:
    // (1 << 3) == 0b0000_0001 << 3
    //  = 0b0000_1000
    // 0b0000_1011 & 0b0000_1000
    //  = 0b0000_1000
    // 0b0000_1000 >> 3
    //  = 0b0000_0001 == 1 == true

    public bool AlreadyHasBuffer(int parallelConnectionIndex)
    {
        return (transferFlag & (1 << parallelConnectionIndex)) >> parallelConnectionIndex == 1;
    }

    public void AddToBuffer(int parallelConnectionIndex, byte[] data)
    {
        int actualDataLength = data.Length - 1;
        byte[] rawData = new byte[actualDataLength];
        Array.Copy(data, 1, rawData, 0, actualDataLength);
        transferBytes[parallelConnectionIndex] = rawData;
        transferFlag |= 1 << parallelConnectionIndex;
        byteCount += actualDataLength;
    }

    public bool BufferFull => transferFlag == MultiplayerConstants.ParallelismFlag;

    public byte[] GetTransferredBytes()
    {
        byte[] bytes = new byte[byteCount];
        int currentIndexInBytes = 0;
        foreach (byte[] peerBytes in transferBytes)
        {
            int length = peerBytes.Length;
            Array.Copy(peerBytes, 0, bytes, currentIndexInBytes, length);
            currentIndexInBytes += length;
        }
        return bytes;
    }

    public static void ZeroPadEvenly(StringBuilder builder, int maxLength, string center)
    {
        int freeSpace = Math.Abs(maxLength-center.Length);
        if (freeSpace % 2 == 0)
        {
            int sideSpaces = freeSpace / 2;
            for (int i = 0; i < sideSpaces; i++)
            {
                builder.Append(' ');
            }
            builder.Append(center);
            for (int i = 0; i < sideSpaces; i++)
            {
                builder.Append(' ');
            }
        }
        else
        {
            int sideSpaces = freeSpace / 2;
            for (int i = 0; i < sideSpaces+1; i++)
            {
                builder.Append(' ');
            }
            builder.Append(center);
            for (int i = 0; i < sideSpaces; i++)
            {
                builder.Append(' ');
            }
        }
    }

    public override string ToString()
    {
        StringBuilder primaryInfoBuilder = new StringBuilder($"Byte Count: {byteCount}\nTransfer Flag:{transferFlag}");
        StringBuilder indexLine = new StringBuilder("\n");
        StringBuilder sizeLine = new StringBuilder("\n");
        StringBuilder builder = new StringBuilder("\n");
        for (int i = 0; i < transferBytes.Length; i++)
        {
            int bytes = transferBytes[i].Length-1;
            int bytesLength = (bytes + "").Length;
            string result = AlreadyHasBuffer(i) ? "X" : " ";

            indexLine.Append('|');
            builder.Append('|');
            sizeLine.Append('|');

            ZeroPadEvenly(indexLine, bytesLength, "" + i);
            ZeroPadEvenly(builder, bytesLength, result);
            ZeroPadEvenly(sizeLine, bytesLength, bytes + "");
        }

        indexLine.Append('|');
        builder.Append('|');
        sizeLine.Append('|');
        return primaryInfoBuilder.Append(indexLine).Append(builder).Append(sizeLine).ToString();
    }
}
