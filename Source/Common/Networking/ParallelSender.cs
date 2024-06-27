using System;
using LiteNetLib;
using Verse;

namespace Multiplayer.Common;

public class ParallelSender
{
    public readonly NetPeer?[] parallelPeers = new NetPeer[MultiplayerConstants.Parallelism-1];
    public int peerCount;

    public void AddPeer(NetPeer peer)
    {
        parallelPeers[peerCount++] = peer;
    }

    private static byte[] GetPeerBytes(int transferIndex, byte[] rawBytes, ref int currentIndex, int sizeOfTransfer)
    {
        byte[] outgoingBytes = new byte[sizeOfTransfer+1];
        outgoingBytes[0] = (byte) transferIndex;
        Array.Copy(rawBytes, currentIndex, outgoingBytes, 1, sizeOfTransfer);
        currentIndex += sizeOfTransfer;
        return outgoingBytes;
    }

    public void Send(byte[] rawBytes, bool reliable)
    {
        int byteLength = rawBytes.Length;
        int lastParallelPeer = peerCount - 1;
        int subdivisionSize = byteLength / lastParallelPeer;

        int positionOfTransfer = 0;

        for (int i = 0; i < lastParallelPeer; i++)
        {
            byte[] outgoingBytes = GetPeerBytes(i, rawBytes, ref positionOfTransfer, subdivisionSize);
            parallelPeers[i]?.Send(outgoingBytes, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
            //Log.Message($"Sending in parallel to peer {parallelPeers[i].EndPoint} with {outgoingBytes.Length}");
        }

        int leftOverAmount = byteLength - positionOfTransfer;
        byte[] finalOutgoingBytes = GetPeerBytes(lastParallelPeer, rawBytes, ref positionOfTransfer, leftOverAmount);
        parallelPeers[lastParallelPeer]?.Send(finalOutgoingBytes, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
        //Log.Message($"Sending in parallel to last peer {parallelPeers[lastParallelPeer].EndPoint} with {leftOverAmount}");
    }

    public void Clear()
    {
        for (int i = 0; i < peerCount; i++)
        {
            parallelPeers[i] = null;
        }
        peerCount = 0;
    }
}
