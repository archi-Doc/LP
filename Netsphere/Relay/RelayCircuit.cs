﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Packet;

namespace Netsphere.Relay;

public class RelayCircuit
{
    public RelayCircuit(NetTerminal netTerminal, IRelayControl relayControl)
    {
        this.netTerminal = netTerminal;
        this.relayControl = relayControl;
    }

    #region FieldAndProperty

    public int NumberOfRelays
        => this.relayNodes.Count;

    private readonly NetTerminal netTerminal;
    private readonly IRelayControl relayControl;
    private readonly RelayNode.GoshujinClass relayNodes = new();

    #endregion

    public async Task<RelayResult> AddRelay(NetNode netNode, CancellationToken cancellationToken = default)
    {
        lock (this.relayNodes.SyncObject)
        {
            var result = this.CanAddRelayInternal(netNode);
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

            // this.relayControl.CreateRelay(clientConnection);

            var block = new CreateRelayBlock((ushort)RandomVault.Pseudo.NextUInt32());
            var r = await clientConnection.SendAndReceive<CreateRelayBlock, CreateRelayResponse>(block, CreateRelayBlock.DataId, cancellationToken).ConfigureAwait(false);
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
                var result = this.CanAddRelayInternal(netNode);
                if (result != RelayResult.Success)
                {//Terminate
                    return result;
                }

                this.relayNodes.Add(new(block.RelayId, netNode));
            }

            return RelayResult.Success;
        }
    }

    public RelayResult AddRelay(NetNode netNode, ushort relayId)
    {
        lock (this.relayNodes.SyncObject)
        {
            var result = this.CanAddRelayInternal(netNode);
            if (result != RelayResult.Success)
            {
                return result;
            }

            this.relayNodes.Add(new(relayId, netNode));
            return RelayResult.Success;
        }
    }

    public RelayResult CanAddRelay(NetNode netNode)
    {
        lock (this.relayNodes.SyncObject)
        {
            return this.CanAddRelayInternal(netNode);
        }
    }

    internal async Task Terminate(CancellationToken cancellationToken)
    {
    }

    private RelayResult CanAddRelayInternal(NetNode netNode)
    {// lock (this.relayNodes.SyncObject)
        if (this.relayNodes.Count >= this.relayControl.MaxSerialRelays)
        {
            return RelayResult.SerialRelayLimit;
        }
        else if (this.relayNodes.NetNodeChain.ContainsKey(netNode))
        {
            return RelayResult.DuplicateNetNode;
        }

        return RelayResult.Success;
    }
}