// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;

namespace Netsphere;

public class ServerTerminal : NetTerminal
{
    public const ushort DefaultReceiverNumber = 4;
    public const ushort MaxReceiverNumber = 16;

    internal ServerTerminal(Terminal terminal, NodeInformation nodeInformation, ulong gene)
        : base(terminal, nodeInformation, gene)
    {// NodeInformation: Managed
    }

    public void SetReceiverNumber(ushort receiverNumber = DefaultReceiverNumber)
    {
        if (receiverNumber > MaxReceiverNumber)
        {
            receiverNumber = MaxReceiverNumber;
        }
        else if (receiverNumber == 0)
        {
            receiverNumber = 1;
        }

        this.ReceiverNumber = receiverNumber;
        this.EnsureReceiver();
    }

    public unsafe override async Task SendClose()
    {// Checked
        if (this.IsClosed)
        {
            return;
        }

        this.IsClosed = true;
        if (!this.IsEncrypted)
        {// Not encrypted (connected)
            return;
        }

        if (this.senderQueue.TryDequeue(out var operation))
        {
            operation.SendClose();
        }
    }

    public async Task<NetResult> SendEmpty()
    {// Checked
        return await this.SendDataAsync(0, Array.Empty<byte>());
    }

    public async Task<NetReceivedData> ReceiveAsync()
    {// Checked
        this.EnsureReceiver();
        if (!this.receiverQueue.TryDequeue(out var operation))
        {
            return new(NetResult.NoReceiver);
        }

        var received = await operation.ReceiveAsync();
        if (received.Result != NetResult.Success)
        {// Timeout or error
            return received;
        }

        this.senderQueue.Enqueue(operation);
        return received;
    }

    public async Task<NetResult> SendPacketAsync<TSend>(TSend value)
        where TSend : IPacket
    {// Checked
        if (!this.senderQueue.TryDequeue(out var operation))
        {
            return NetResult.NoSender;
        }

        var result = await operation.SendPacketAsync(value);
        await operation.DisposeAsync();
        return result;
    }

    public async Task<NetResult> SendAsync<TSend>(TSend value)
    {// Checked
        if (!this.senderQueue.TryDequeue(out var operation))
        {
            return NetResult.NoSender;
        }

        var result = await operation.SendAsync(value);
        await operation.DisposeAsync();
        return result;
    }

    public async Task<NetResult> SendDataAsync(ulong dataId, ByteArrayPool.MemoryOwner data)
    {// Checked
        if (!this.senderQueue.TryDequeue(out var operation))
        {
            return NetResult.NoSender;
        }

        var result = await operation.SendDataAsync(true, PacketId.Data, dataId, data).ConfigureAwait(false);
        await operation.DisposeAsync();
        return result;
    }

    public async Task<NetResult> SendDataAsync(ulong dataId, byte[] data)
    {// Checked
        if (!this.senderQueue.TryDequeue(out var operation))
        {
            return NetResult.NoSender;
        }

        var result = await operation.SendDataAsync(true, PacketId.Data, dataId, new ByteArrayPool.MemoryOwner(data)).ConfigureAwait(false);
        await operation.DisposeAsync();
        return result;
    }

    public async Task<NetResult> SendServiceAsync(ulong dataId, ByteArrayPool.MemoryOwner data)
    {// Checked
        if (!this.senderQueue.TryDequeue(out var operation))
        {
            return NetResult.NoSender;
        }

        var result = await operation.SendDataAsync(true, PacketId.Rpc, dataId, data).ConfigureAwait(false);
        await operation.DisposeAsync();
        return result;
    }

    public void EnsureReceiver()
    {// Checked
        while (this.receiverQueue.Count < this.ReceiverNumber)
        {
            this.receiverQueue.Enqueue(new ServerOperation(this));
        }
    }

    public async Task ClearSender()
    {// Checked
        while (this.senderQueue.TryDequeue(out var operation))
        {
            await operation.DisposeAsync();
        }
    }

    public ushort ReceiverNumber { get; private set; } = DefaultReceiverNumber;

    private ConcurrentQueue<ServerOperation> senderQueue = new();
    private ConcurrentQueue<ServerOperation> receiverQueue = new();
}
