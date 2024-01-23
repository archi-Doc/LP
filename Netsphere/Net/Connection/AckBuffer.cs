// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Netsphere.Packet;

namespace Netsphere.Net;

internal partial class AckBuffer
{
    private readonly record struct ConnectionAndAckQueue(Connection connection, Queue<long> queue);

    public AckBuffer(ConnectionTerminal connectionTerminal)
    {
        this.connectionTerminal = connectionTerminal;
        this.logger = connectionTerminal.UnitLogger.GetLogger<AckBuffer>();
    }

    #region FieldAndProperty

    private readonly ConnectionTerminal connectionTerminal;
    private readonly ILogger logger;

    private readonly object syncObject = new();
    private readonly Queue<Queue<ulong>> freeQueue = new();
    private readonly Queue<Connection> connectionQueue = new();

    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(Connection connection, uint transmissionId, int geneSerial)
    {
#if LOG_LOWLEVEL_NET
        this.logger.TryGet(LogLevel.Debug)?.Log($"{this.connectionTerminal.NetTerminal.NetTerminalString} to {connection.EndPoint.ToString()} {transmissionId} {geneSerial}");
#endif

        lock (this.syncObject)
        {
            var queue = connection.AckQueue;
            if (queue is null)
            {
                this.freeQueue.TryDequeue(out queue); // Reuse the queued queue.
                queue ??= new();
                this.connectionQueue.Enqueue(connection);

                connection.AckMics = Mics.FastSystem + NetConstants.AckDelayMics;
                connection.AckQueue = queue;
            }

            queue.Enqueue(((ulong)transmissionId << 32) | (uint)geneSerial);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddRange(Connection connection, uint transmissionId, int geneStart, int geneEnd)
    {
#if LOG_LOWLEVEL_NET
        this.logger.TryGet(LogLevel.Debug)?.Log($"{this.connectionTerminal.NetTerminal.NetTerminalString} to {connection.EndPoint.ToString()} {transmissionId} {geneStart}-{geneEnd}");
#endif

        Debug.Assert((geneEnd - geneStart) <= NetHelper.RamaGenes);

        lock (this.syncObject)
        {
            var queue = connection.AckQueue;
            if (queue is null)
            {
                this.freeQueue.TryDequeue(out queue); // Reuse the queued queue.
                queue ??= new();
                this.connectionQueue.Enqueue(connection);

                connection.AckMics = Mics.FastSystem + NetConstants.AckDelayMics;
                connection.AckQueue = queue;
            }

            for (var i = geneStart; i < geneEnd; i++)
            {
                queue.Enqueue(((ulong)transmissionId << 32) | (uint)i);
            }
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
                if (connection is not null && Mics.FastSystem > connection.AckMics)
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

            this.ProcessAck(netSender, connection, ackQueue);
        }
    }

    private void ProcessAck(NetSender netSender, Connection connection, Queue<ulong> ackQueue)
    {// AckFrameCode
        ByteArrayPool.Owner? owner = default;
        var position = 0;
        var transmissionPosition = 0;
        const int maxLength = NetControl.MaxPacketLength - 16; // remainig = maxLength - position;
        uint previousTransmissionId = 0;
        ushort numberOfPairs = 0;
        int startGene = -1;
        int endGene = -1;

        while (ackQueue.TryDequeue(out var ack))
        {
            if ((maxLength - position) < AckFrame.Margin)
            {// Send the packet due to the size approaching the limit.
                SendPacket();
            }

            if (owner is null)
            {
                owner = PacketPool.Rent();
                transmissionPosition = PacketHeader.Length + AckFrame.Length;
                position = transmissionPosition + 6;
            }

            var transmissionId = (uint)(ack >> 32);
            var geneSerial = (int)ack;

            // this.logger.TryGet(LogLevel.Debug)?.Log($"ProcessAck: {transmissionId}, {geneSerial}");

            if (previousTransmissionId == 0)
            {// Initial transmission id
                previousTransmissionId = transmissionId;
                startGene = geneSerial;
                endGene = geneSerial + 1;
            }
            else if (transmissionId == previousTransmissionId)
            {// Same transmission id
                if (startGene == -1)
                {// Initial gene
                    startGene = geneSerial;
                    endGene = geneSerial + 1;
                }
                else if (endGene == geneSerial)
                {// Serial genes
                    endGene = geneSerial + 1;
                }
                else
                {// Not serial gene
                    var span = owner.ByteArray.AsSpan(position);
                    BitConverter.TryWriteBytes(span, startGene);
                    span = span.Slice(sizeof(int));
                    BitConverter.TryWriteBytes(span, endGene);
                    position += 8;
                    numberOfPairs++;

                    startGene = geneSerial;
                    endGene = geneSerial + 1;
                }
            }
            else
            {// Different transmission id
                // this.logger.TryGet(LogLevel.Debug)?.Log($"SendingAck: {previousTransmissionId}, {startGene} - {endGene}");

                var span = owner.ByteArray.AsSpan(position);
                BitConverter.TryWriteBytes(span, startGene);
                span = span.Slice(sizeof(int));
                BitConverter.TryWriteBytes(span, endGene);
                position += 8;
                numberOfPairs++;

                span = owner.ByteArray.AsSpan(transmissionPosition);
                BitConverter.TryWriteBytes(span, previousTransmissionId);
                span = span.Slice(sizeof(uint));
                BitConverter.TryWriteBytes(span, numberOfPairs);

                previousTransmissionId = transmissionId;
                numberOfPairs = 0;
                transmissionPosition = position;
                position += 6;

                startGene = geneSerial;
                endGene = geneSerial + 1;
            }
        }

        SendPacket();

        void SendPacket()
        {
            if (owner is not null)
            {
                // this.logger.TryGet(LogLevel.Debug)?.Log($"SendingAck: {previousTransmissionId}, {startGene} - {endGene}");

                if (previousTransmissionId != 0)
                {
                    var span = owner.ByteArray.AsSpan(position);
                    BitConverter.TryWriteBytes(span, startGene);
                    span = span.Slice(sizeof(int));
                    BitConverter.TryWriteBytes(span, endGene);
                    position += 8;
                    numberOfPairs++;

                    span = owner.ByteArray.AsSpan(transmissionPosition);
                    BitConverter.TryWriteBytes(span, previousTransmissionId);
                    span = span.Slice(sizeof(uint));
                    BitConverter.TryWriteBytes(span, numberOfPairs);

                    previousTransmissionId = 0;
                    numberOfPairs = 0;
                    transmissionPosition = 0;
                    startGene = -1;
                    endGene = -1;
                }

                connection.CreateAckPacket(owner, position - PacketHeader.Length, out var packetLength);
                netSender.Send_NotThreadSafe(connection.EndPoint.EndPoint, owner.ToMemoryOwner(0, packetLength));
                // owner = owner.Return(); // Moved
            }
        }
    }
}
