﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Arc.Collections;
using Netsphere.Packet;
using static Arc.Unit.ByteArrayPool;

namespace Netsphere.Net;

internal partial class AckBuffer
{
    private readonly record struct ConnectionAndAckQueue(Connection connection, Queue<long> queue);

    public AckBuffer()
    {
    }

    #region FieldAndProperty

    private readonly object syncObject = new();
    private readonly Queue<Queue<ulong>> freeQueue = new();
    private readonly Queue<Connection> connectionQueue = new();

    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(Connection connection, uint transmissionId, int geneSerial)
    {
        lock (this.syncObject)
        {
            var queue = connection.AckQueue;
            if (queue is null)
            {
                this.freeQueue.TryDequeue(out queue);
                queue ??= new();
                this.connectionQueue.Enqueue(connection);
                connection.AckMics = Mics.GetSystem() + NetConstants.AckDelayMics;
                connection.AckQueue = queue;
            }

            queue.Enqueue((transmissionId << 32) | (uint)geneSerial);
        }
    }

    public void ProcessSend(NetSender netSender)
    {
        Connection? connection = default;
        Queue<ulong>? ackQueue = default;

        while (netSender.CanSend)
        {
            lock (this.syncObject)
            {
                if (ackQueue is not null)
                {
                    this.freeQueue.Enqueue(ackQueue);
                    ackQueue = default;
                }

                this.connectionQueue.TryPeek(out connection);
                if (connection is not null && netSender.CurrentSystemMics > connection.AckMics)
                {
                    this.connectionQueue.Dequeue();
                    ackQueue = connection.AckQueue;
                    connection.AckMics = 0;
                    connection.AckQueue = default;
                }
            }

            if (connection is null || ackQueue is null)
            {
                break;
            }

            this.ProcessSend(netSender, connection, ackQueue);
        }
    }

    private void ProcessSend(NetSender netSender, Connection connection, Queue<ulong> ackQueue)
    {// AckFrameCode
        ByteArrayPool.Owner? owner = default;
        var position = 0; // remainig = NetControl.MaxPacketLength - 16 - position;
        var remaining = 0;
        uint previousTransmissionId = 0;
        int numberOfPairs = 0;
        int startGene = -1;
        int endGene = -1;

        while (ackQueue.TryDequeue(out var ack))
        {
            if (remaining < AckFrame.Margin)
            {// Send the packet due to the size approaching the limit.
                SendPacket();
            }

            if (owner is null)
            {
                owner = PacketPool.Rent();
                position = PacketHeader.Length + AckFrame.Length;
                remaining = NetControl.MaxPacketLength - 16 - position;
            }

            var transmissionId = (uint)(ack >> 32);
            var geneSerial = (int)ack;

            if (previousTransmissionId == 0)
            {// Initial transmission id
                previousTransmissionId = transmissionId;
                startGene = geneSerial;
                endGene = geneSerial;
            }
            else if (transmissionId == previousTransmissionId)
            {// Same transmission id
                if (startGene == -1 && endGene == -1)
                {// Initial gene
                    startGene = geneSerial;
                    endGene = geneSerial;
                }
                else if (endGene == geneSerial - 1)
                {// Serial genes
                    endGene = geneSerial;
                }
                else
                {// Not serial
                    var span = owner.ByteArray.AsSpan(position);
                    BitConverter.TryWriteBytes(span, startGene);
                    span = span.Slice(sizeof(int));
                    BitConverter.TryWriteBytes(span, endGene);
                    position += 8;
                    remaining -= 8;
                    numberOfPairs++;

                    startGene = -1;
                    endGene = -1;
                }
            }
            else
            {// Different transmission id
                previousTransmissionId = transmissionId;
            }
        }

        SendPacket();

        void SendPacket()
        {
            if (owner is not null)
            {
                if (previousTransmissionId != 0)
                {

                }

                connection.CreateAckPacket(owner, length, out var packetLength);
                netSender.Send_NotThreadSafe(connection.EndPoint.EndPoint, owner.ToMemoryOwner(0, packetLength));
                owner = owner.Return();
            }
        }
    }
}
