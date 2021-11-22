﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace LP.Net;

/// <summary>
/// Initializes a new instance of the <see cref="NetTerminal"/> class.<br/>
/// NOT thread-safe.
/// </summary>
[ValueLinkObject]
public partial class NetTerminal : IDisposable
{
    public const int DefaultMillisecondsToWait = 2000;

    /// <summary>
    /// The default interval time in milliseconds.
    /// </summary>
    public const int DefaultInterval = 10;

    [Link(Type = ChainType.QueueList, Name = "Queue", Primary = true)]
    internal NetTerminal(Terminal terminal, NodeAddress nodeAddress)
    {// NodeAddress: Unmanaged
        this.Terminal = terminal;
        this.GenePool = new(Random.Crypto.NextULong());
        this.NodeAddress = nodeAddress;
        this.Endpoint = this.NodeAddress.CreateEndpoint();
    }

    internal NetTerminal(Terminal terminal, NodeInformation nodeInformation)
        : this(terminal, nodeInformation, Random.Crypto.NextULong())
    {// NodeInformation: Managed
    }

    internal NetTerminal(Terminal terminal, NodeInformation nodeInformation, ulong gene)
    {// NodeInformation: Encrypted
        this.Terminal = terminal;
        this.GenePool = new(gene);
        this.NodeAddress = nodeInformation;
        this.NodeInformation = nodeInformation;
        this.Endpoint = this.NodeAddress.CreateEndpoint();
    }

    public Terminal Terminal { get; }

    public bool IsClosed => this.disposed;

    // [Link(Type = ChainType.Ordered)]
    // public long CreatedTicks { get; private set; } = Ticks.GetCurrent();

    public IPEndPoint Endpoint { get; }

    public NodeAddress NodeAddress { get; }

    public NodeInformation? NodeInformation { get; }

    public INetInterface<TSend> SendRaw<TSend>(TSend value)
        where TSend : IRawPacket
    {
        var netInterface = this.SendPacket(value);
        lock (this.SyncObject)
        {
            this.netInterfaces.Add(netInterface);
        }

        return netInterface;
    }

    public INetInterface<TSend, TReceive> SendAndReceiveRaw<TSend, TReceive>(TSend value)
        where TSend : IRawPacket
    {
        var netInterface = this.SendAndReceivePacket<TSend, TReceive>(value);
        lock (this.SyncObject)
        {
            this.netInterfaces.Add(netInterface);
        }

        return netInterface;
    }

    /*public enum SendResult
    {
        Success,
        Error,
        Timeout,
    }

    public SendResult CheckManagedAndEncrypted()
    {
        if (this.embryo != null)
        {// Encrypted
            return SendResult.Success;
        }
        else if (this.NodeInformation == null)
        {// Unmanaged
            return SendResult.Error;
        }

        // var p = new PacketEncrypt(this.Terminal.NetStatus.GetMyNodeInformation());
        var p = new RawPacketEncrypt(this.Terminal.NetStatus.GetMyNodeInformation());
        this.SendPacket(p);
        var r = this.ReceiveRaw<RawPacketEncrypt>();
        if (r != null)
        {
            if (this.CreateEmbryo(p.Salt))
            {
                return SendResult.Success;
            }
            else
            {
                return SendResult.Error;
            }
        }

        return SendResult.Timeout;
    }*/

    /*public SendResult Send<T>(T value, int millisecondsToWait = DefaultMillisecondsToWait)
        where T : IRawPacket, IPacket
    {
        var result = this.CheckManagedAndEncrypted();
        if (result != SendResult.Success)
        {
            return result;
        }

        return this.SendPacket(value);
    }*/

    internal GenePool GenePool { get; }

    internal void CreateHeader(out RawPacketHeader header, ulong gene)
    {
        header = default;
        header.Gene = gene;
        header.Engagement = this.NodeAddress.Engagement;
    }

    internal NetInterface<TSend, object> SendPacket<TSend>(TSend value)
        where TSend : IRawPacket
    {
        var netInterface = new NetInterface<TSend, object>(this);
        netInterface.Initialize(value, value.Id, false);
        return netInterface;
    }

    internal NetInterface<TSend, TReceive> SendAndReceivePacket<TSend, TReceive>(TSend value)
        where TSend : IRawPacket
    {
        var netInterface = new NetInterface<TSend, TReceive>(this);
        netInterface.Initialize(value, value.Id, true);
        return netInterface;
    }

    internal void ProcessSend(UdpClient udp, long currentTicks)
    {
        lock (this.SyncObject)
        {
            foreach (var x in this.netInterfaces)
            {
                x.ProcessSend(udp, currentTicks);
            }
        }
    }

    internal object SyncObject { get; } = new();

    internal ISimpleLogger? TerminalLogger => this.Terminal.TerminalLogger;

    internal bool CreateEmbryo(ulong salt)
    {
        Logger.Default.Information($"Salt {salt.ToString()}");
        if (this.NodeInformation == null)
        {
            return false;
        }

        var ecdh = NodeKey.FromPublicKey(this.NodeInformation.PublicKeyX, this.NodeInformation.PublicKeyY);
        if (ecdh == null)
        {
            return false;
        }

        var material = this.Terminal.NodePrivateECDH.DeriveKeyMaterial(ecdh.PublicKey);
        Span<byte> buffer = stackalloc byte[sizeof(ulong) + NodeKey.PrivateKeySize + sizeof(ulong)];
        var span = buffer;
        BitConverter.TryWriteBytes(span, salt);
        span = span.Slice(sizeof(ulong));
        material.AsSpan().CopyTo(span);
        span = span.Slice(NodeKey.PrivateKeySize);
        BitConverter.TryWriteBytes(span, salt);

        var sha = Hash.Sha3_384Pool.Get();
        this.embryo = sha.GetHash(buffer);
        Hash.Sha3_384Pool.Return(sha);

        Logger.Default.Information($"embryo {this.embryo[0].ToString()}");
        this.GenePool.SetEmbryo(this.embryo);
        Logger.Default.Information($"First gene {this.GenePool.GetGene().ToString()}");

        return true;
    }

    private void Clear()
    {// lock (this.SyncObject)
        foreach (var x in this.netInterfaces)
        {
            x.Clear();
        }
    }

    private List<NetInterface> netInterfaces = new();
    private byte[]? embryo;

    // private PacketService packetService = new();

#pragma warning disable SA1124 // Do not use regions
    #region IDisposable Support
#pragma warning restore SA1124 // Do not use regions

    private bool disposed = false; // To detect redundant calls.

    /// <summary>
    /// Finalizes an instance of the <see cref="NetTerminal"/> class.
    /// </summary>
    ~NetTerminal()
    {
        this.Dispose(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// free managed/native resources.
    /// </summary>
    /// <param name="disposing">true: free managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed)
        {
            if (disposing)
            {
                // free managed resources.
                this.Terminal.TryRemove(this);
                lock (this.SyncObject)
                {
                    this.Clear();
                }
            }

            // free native resources here if there are any.
            this.disposed = true;
        }
    }
    #endregion
}
