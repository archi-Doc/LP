// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

public class ClientTerminal : NetTerminal
{
    internal ClientTerminal(Terminal terminal, NodeAddress nodeAddress)
        : base(terminal, nodeAddress)
    {// NodeAddress: Unmanaged
    }

    internal ClientTerminal(Terminal terminal, NodeInformation nodeInformation)
        : base(terminal, nodeInformation, LP.Random.Crypto.NextUInt64())
    {// NodeInformation: Managed
    }

    public override async Task<NetResult> EncryptConnectionAsync()
    {// Checked
        if (this.IsEncrypted)
        {// Encrypted
            return NetResult.Success;
        }
        else if (this.NodeInformation == null && !this.Terminal.NetBase.AllowUnsafeConnection)
        {// Unmanaged
            return NetResult.NoNodeInformation;
        }

        await using (var operation = this.CreateOperation())
        {
            return await operation.EncryptConnectionAsync().ConfigureAwait(false);
        }
    }

    public override async Task SendClose()
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

        await using(var operation = new ClientOperation(this))
        {
            this.CreateHeader(out var header, operation.GetGene());
            header.Id = PacketId.Close;

            this.HeaderToMemoryOwner(ref header, out var owner);

            this.Terminal.AddRawSend(this.Endpoint, owner);
        }
    }

    public async Task<NetResult> SendPacketAsync<TSend>(TSend value)
        where TSend : IPacket
    {// Checked
        await using(var operation = this.CreateOperation())
        {
            return await operation.SendPacketAsync(value).ConfigureAwait(false);
        }
    }

    public async Task<(NetResult Result, TReceive? Value)> SendPacketAndReceiveAsync<TSend, TReceive>(TSend value)
        where TSend : IPacket
    {// Checked
        await using (var operation = this.CreateOperation())
        {
            return await operation.SendPacketAndReceiveAsync<TSend, TReceive>(value).ConfigureAwait(false);
        }
    }

    public async Task<NetResult> SendAsync<TSend>(TSend value)
    {// Checked
        await using (var operation = this.CreateOperation())
        {
            return await operation.SendAsync(value);
        }
    }

    public async Task<NetResult> SendDataAsync(ulong dataId, ByteArrayPool.MemoryOwner data)
    {// Checked
        await using (var operation = this.CreateOperation())
        {
            return await operation.SendDataAsync(true, PacketId.Data, dataId, data).ConfigureAwait(false);
        }
    }

    public async Task<NetResult> SendDataAsync(ulong dataId, byte[] data)
    {// Checked
        await using (var operation = this.CreateOperation())
        {
            return await operation.SendDataAsync(true, PacketId.Data, dataId, new ByteArrayPool.MemoryOwner(data)).ConfigureAwait(false);
        }
    }

    public async Task<NetResult> SendServiceAsync(ulong dataId, ByteArrayPool.MemoryOwner data)
    {// Checked
        await using (var operation = this.CreateOperation())
        {
            return await operation.SendDataAsync(true, PacketId.Rpc, dataId, data).ConfigureAwait(false);
        }
    }

    public async Task<(NetResult Result, TReceive? Value)> SendAndReceiveAsync<TSend, TReceive>(TSend value)
    {// Checked
        await using (var operation = this.CreateOperation())
        {
            return await operation.SendAndReceiveAsync<TSend, TReceive>(value);
        }
    }

    public async Task<(NetResult Result, ByteArrayPool.MemoryOwner Value)> SendAndReceiveDataAsync(ulong dataId, ByteArrayPool.MemoryOwner data)
    {// Checked
        await using (var operation = this.CreateOperation())
        {
            var response = await operation.SendAndReceiveDataAsync(true, PacketId.Data, dataId, data).ConfigureAwait(false);
            return (response.Result, response.Received);
        }
    }

    public async Task<(NetResult Result, ByteArrayPool.MemoryOwner Value)> SendAndReceiveDataAsync(ulong dataId, byte[] data)
    {// Checked
        await using (var operation = this.CreateOperation())
        {
            var response = await operation.SendAndReceiveDataAsync(true, PacketId.Data, dataId, new ByteArrayPool.MemoryOwner(data)).ConfigureAwait(false);
            return (response.Result, response.Received);
        }
    }

    public async Task<(NetResult Result, ByteArrayPool.MemoryOwner Value)> SendAndReceiveServiceAsync(ulong dataId, ByteArrayPool.MemoryOwner data)
    {// Checked
        await using (var operation = this.CreateOperation())
        {
            var response = await operation.SendAndReceiveDataAsync(true, PacketId.Rpc, dataId, data).ConfigureAwait(false);
            return (response.Result, response.Received);
        }
    }

    public TService GetService<TService>()
        where TService : INetService
    {
        return StaticNetService.CreateClient<TService>(this);
    }

    internal ClientOperation CreateOperation() => new ClientOperation(this);
}
