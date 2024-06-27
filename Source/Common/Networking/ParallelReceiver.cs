using System;
using System.Collections.Generic;
using LiteNetLib;
using Verse;

namespace Multiplayer.Common;

public class ParallelReceiver
{
    protected readonly LinkedList<ParallelTransferBuffer> transferBuffers = new();
    private readonly object lockValue = new();

    public (ByteReader reader, bool passAlong) OnParallelNetworkReceive(byte[] data)
    {
        try
        {
            int transferIndex = data[0];
            lock (lockValue) // ensure no concurrent modifications happen causing serial logic to fail (such as if the buffer is full yet.
            {
                // Could do further concurrency optimisation, but this should work for now.
                bool addedToBuffer = false;

                LinkedListNode<ParallelTransferBuffer> current = transferBuffers.First;
                while (current != null)
                {
                    if (current.Value.AlreadyHasBuffer(transferIndex))
                    {
                        if (current.Next == null)
                        {
                            break;
                        }

                        current = current.Next;
                        continue;
                    }
                    current.Value.AddToBuffer(transferIndex, data);
                    addedToBuffer = true;
                    if (current.Value.BufferFull)
                    {
                        Log.Message("Transferring to handler because buffer full.");
                        transferBuffers.Remove(current);
                        return (new ByteReader(current.Value.GetTransferredBytes()), true);
                    }

                    break;
                }
                if (!addedToBuffer || transferBuffers.Count == 0)
                {
                    ParallelTransferBuffer transferBuffer = new ParallelTransferBuffer(transferIndex, data);
                    if (transferBuffer.BufferFull)
                    {
                        return (new ByteReader(transferBuffer.GetTransferredBytes()), true);
                    }
                    transferBuffers.AddLast(transferBuffer);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error occurred... {e.StackTrace}");
        }

        return (new ByteReader([]), false);
    }

    public override string ToString()
    {
        return base.ToString() + ": " + GetHashCode();
    }
}
