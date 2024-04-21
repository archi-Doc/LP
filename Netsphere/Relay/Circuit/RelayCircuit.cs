﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Netsphere.Packet;

namespace Netsphere.Relay;

/// <summary>
/// <see cref="RelayCircuit"/> is a primitive class for managing relay circuits.
/// </summary>
public class RelayCircuit
{
    public RelayCircuit(NetTerminal netTerminal, IRelayControl relayControl)
    {
        this.netTerminal = netTerminal;
        this.relayControl = relayControl;
    }

    #region FieldAndProperty

    public bool IsRelayAvailable
        => this.relayNodes.Count > 0;

    public int NumberOfRelays
        => this.relayNodes.Count;

    private readonly NetTerminal netTerminal;
    private readonly IRelayControl relayControl;
    private readonly RelayNode.GoshujinClass relayNodes = new();

    private Aes[]? aes0 = null;
    private Aes[]? aes1 = null;

    #endregion

    /*public async Task<RelayResult> AddRelay(NetNode netNode, CancellationToken cancellationToken = default)
    {
        var relayId = (ushort)RandomVault.Pseudo.NextUInt32();
        lock (this.relayNodes.SyncObject)
        {
            var result = this.CanAddRelayInternal(relayId, netNode);
            if (result != RelayResult.Success)
            {
                return result;
            }
        }

        using (var clientConnection = await this.netTerminal.Connect(netNode, Connection.ConnectMode.NoReuse).ConfigureAwait(false))
        {
            if (clientConnection is null)
            {
                return RelayResult.ConnectionFailure;
            }

            var r = await clientConnection.SendAndReceive<CreateRelayBlock, CreateRelayResponse>(new CreateRelayBlock(relayId), 0, cancellationToken).ConfigureAwait(false);
            if (r.IsFailure || r.Value is null)
            {
                return RelayResult.ConnectionFailure;
            }
            else if (r.Value.Result != RelayResult.Success)
            {
                return r.Value.Result;
            }

            lock (this.relayNodes.SyncObject)
            {
                var result = this.CanAddRelayInternal(relayId, netNode);
                if (result != RelayResult.Success)
                {//Terminate
                    return result;
                }

                this.relayNodes.Add(new(relayId, clientConnection));
            }

            return RelayResult.Success;
        }
    }*/

    public RelayResult AddRelay(ushort relayId, ClientConnection clientConnection)
    {
        lock (this.relayNodes.SyncObject)
        {
            var result = this.CanAddRelayInternal(relayId, clientConnection.DestinationNode);
            if (result != RelayResult.Success)
            {
                return result;
            }

            this.relayNodes.Add(new(relayId, clientConnection));
            return RelayResult.Success;
        }
    }

    /*public void Encrypt(ref ByteArrayPool.MemoryOwner owner)
    {
        if (!this.IsRelayAvailable)
        {
            return;
        }

        var aes = this.GetAes();
        if (aes.Length == 0)
        {
            this.ReturnAes(aes);
            return;
        }

        var prev = owner;
        prev = PacketPool.Rent();
        for (var i = aes.Length - 1; i >= 0; i--)
        {
            Span<byte> iv = stackalloc byte[16];
            this.embryo.Iv.CopyTo(iv);
            BitConverter.TryWriteBytes(iv, salt);
            var result = aes[i].TryEncryptCbc(owner.Span, iv, MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(span), spanMax), out written, PaddingMode.PKCS7);
        }

        this.ReturnAes(aes);
    }

    public async Task<NetResultValue<TReceive>> SendAndReceive<TSend, TReceive>(int relayIndex, TSend data, ulong dataId = 0, CancellationToken cancellationToken = default)
    {
        var aesList = new Aes[0];
        lock (this.relayNodes.SyncObject)
        {
            if (relayIndex < 0 || relayIndex >= this.relayNodes.Count)
            {
                return new(NetResult.InvalidData);
            }
        }
    }*/

    public RelayResult CanAddRelay(ushort relayId, NetNode netNode)
    {
        lock (this.relayNodes.SyncObject)
        {
            return this.CanAddRelayInternal(relayId, netNode);
        }
    }

    internal async Task Terminate(CancellationToken cancellationToken)
    {
    }

    private RelayResult CanAddRelayInternal(ushort relayId, NetNode netNode)
    {// lock (this.relayNodes.SyncObject)
        if (this.relayNodes.Count >= this.relayControl.MaxSerialRelays)
        {
            return RelayResult.SerialRelayLimit;
        }
        else if (this.relayNodes.RelayIdChain.ContainsKey(relayId))
        {
            return RelayResult.DuplicateRelayId;
        }
        else if (this.relayNodes.NetNodeChain.ContainsKey(netNode))
        {
            return RelayResult.DuplicateNetNode;
        }

        return RelayResult.Success;
    }
}