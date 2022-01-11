// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace Netsphere;

#pragma warning disable SA1401 // Fields should be private

/// <summary>
/// Initializes a new instance of the <see cref="NetTerminal"/> class.<br/>
/// NOT thread-safe.
/// </summary>
[ValueLinkObject]
public partial class NetTerminal : IDisposable
{
    /// <summary>
    /// The default interval time in milliseconds.
    /// </summary>
    public const int DefaultInterval = 10;

    [Link(Type = ChainType.QueueList, Name = "Queue", Primary = true)]
    internal NetTerminal(Terminal terminal, NodeAddress nodeAddress)
    {// NodeAddress: Unmanaged
        this.Terminal = terminal;
        this.GenePool = new(LP.Random.Crypto.NextUInt64());
        this.NodeAddress = nodeAddress;
        this.Endpoint = this.NodeAddress.CreateEndpoint();

        this.FlowControl = new(this);

        this.InitializeState();
    }

    internal NetTerminal(Terminal terminal, NodeInformation nodeInformation)
        : this(terminal, nodeInformation, LP.Random.Crypto.NextUInt64())
    {// NodeInformation: Managed
    }

    internal NetTerminal(Terminal terminal, NodeInformation nodeInformation, ulong gene)
    {// NodeInformation: Encrypted
        this.Terminal = terminal;
        this.GenePool = new(gene);
        this.NodeAddress = nodeInformation;
        this.NodeInformation = nodeInformation;
        this.Endpoint = this.NodeAddress.CreateEndpoint();

        this.FlowControl = new(this);

        this.InitializeState();
    }

    public void SetMaximumResponseTime(int milliseconds = 500)
    {
        this.maximumResponseMics = Mics.FromMilliseconds(milliseconds);
    }

    public long MaximumResponseMics => this.maximumResponseMics;

    public void SetMinimumBandwidth(double megabytesPerSecond = 0.1)
    {
        this.minimumBandwidth = megabytesPerSecond;
    }

    public double MinimumBandwidth => this.minimumBandwidth;

    public virtual async Task<NetResult> EncryptConnectionAsync() => NetResult.NoEncryptedConnection;

    public virtual async Task SendClose()
    {
    }

    public Terminal Terminal { get; }

    public FlowControl FlowControl { get; }

    public bool IsEncrypted => this.embryo != null;

    public bool IsSendComplete => false;

    public bool IsReceiveComplete => false;

    public bool IsHighTraffic => false;

    public bool IsClosed { get; internal set; }

    public IPEndPoint Endpoint { get; }

    public NodeAddress NodeAddress { get; }

    public NodeInformation? NodeInformation { get; protected set; }

    internal void InternalClose()
    {
        this.IsClosed = true;
    }

    internal void MergeNodeInformation(NodeInformation nodeInformation)
    {
        this.NodeInformation = Netsphere.NodeInformation.Merge(this.NodeAddress, nodeInformation);
    }

    internal void CreateHeader(out PacketHeader header, ulong gene)
    {
        header = default;
        header.Gene = gene;
        header.Engagement = this.NodeAddress.Engagement;
    }

    internal unsafe void HeaderToMemoryOwner(ref PacketHeader header, out ByteArrayPool.MemoryOwner owner)
    {
        var arrayOwner = PacketPool.Rent();
        fixed (byte* bp = arrayOwner.ByteArray)
        {
            *(PacketHeader*)bp = header;
        }

        owner = arrayOwner.ToMemoryOwner();
    }

    internal unsafe void SendAck(ulong gene)
    {
        this.CreateHeader(out var header, gene);
        header.Id = PacketId.Ack;

        var arrayOwner = PacketPool.Rent();
        fixed (byte* bp = arrayOwner.ByteArray)
        {
            *(PacketHeader*)bp = header;
        }

        this.Terminal.AddRawSend(this.Endpoint, arrayOwner.ToMemoryOwner(0, PacketService.HeaderSize));
    }

    internal void ProcessSend(UdpClient udp, long currentMics)
    {
        lock (this.SyncObject)
        {
            if (this.IsClosed)
            {
                return;
            }

            this.FlowControl.Update(currentMics);
            this.FlowControl.RentSendCapacity(out var sendCapacity);
            // this.Terminal.TerminalLogger?.Information(sendCapacity.ToString());
            foreach (var x in this.activeInterfaces)
            {
                if (sendCapacity == 0)
                {
                    break;
                }

                x.ProcessSend(udp, currentMics, ref sendCapacity);
            }

            this.FlowControl.ReturnSendCapacity(sendCapacity);

            // Send Ack
            if ((currentMics - this.lastSendingAckMics) > Mics.FromMilliseconds(NetConstants.SendingAckIntervalInMilliseconds))
            {
                this.lastSendingAckMics = currentMics;

                foreach (var x in this.activeInterfaces)
                {
                    x.ProcessSendingAck();
                }

                foreach (var x in this.disposedInterfaces)
                {
                    x.ProcessSendingAck();
                }
            }
        }
    }

    internal void Add(NetInterface netInterface)
    {
        lock (this.SyncObject)
        {
            this.activeInterfaces.Add(netInterface);
        }
    }

    internal bool RemoveInternal(NetInterface netInterface)
    {// lock (this.SyncObject)
        if (netInterface.DisposedMics == 0)
        {// Active
            return this.activeInterfaces.Remove(netInterface);
        }
        else
        {// Disposed
            return this.disposedInterfaces.Remove(netInterface);
        }
    }

    internal NetResult ReportResult(NetResult result)
    {
        return result;
    }

    internal object SyncObject { get; } = new();

    internal SemaphoreSlim ConnectionSemaphore { get; } = new(1, 1);

    internal GenePool GenePool { get; }

    internal ISimpleLogger? TerminalLogger => this.Terminal.TerminalLogger;

    internal NetResult CreateEmbryo(ulong salt)
    {
        if (this.NodeInformation == null)
        {
            return NetResult.NoNodeInformation;
        }

        lock (this.SyncObject)
        {
            if (this.embryo != null)
            {
                return NetResult.Success;
            }

            var ecdh = NodeKey.FromPublicKey(this.NodeInformation.PublicKeyX, this.NodeInformation.PublicKeyY);
            if (ecdh == null)
            {
                return NetResult.NoNodeInformation;
            }

            // ulong Salt, byte[] material, ulong Salt
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

            this.GenePool.SetEmbryo(this.embryo);
            this.TerminalLogger?.Information($"First gene {this.GenePool.GetSequential().To4Hex()}");

            // Aes
            this.aes = Aes.Create();
            this.aes.KeySize = 256;
            this.aes.Key = this.embryo.AsSpan(0, 32).ToArray();
        }

        return NetResult.Success;
    }

    internal bool TryClean(long currentMics)
    {
        if (this.IsClosed)
        {
            return true;
        }

        var mics = currentMics - Mics.FromSeconds(2);
        List<NetInterface>? list = null;

        lock (this.SyncObject)
        {
            foreach (var x in this.disposedInterfaces)
            {
                if (x.DisposedMics < mics)
                {
                    list ??= new();
                    list.Add(x);
                }
            }

            if (list != null)
            {
                foreach (var x in list)
                {
                    x.DisposeActual();
                }
            }
        }

        return false;
    }

    internal void ActiveToDisposed(NetInterface netInterface)
    {// lock (this.SyncObject)
        this.activeInterfaces.Remove(netInterface);
        this.disposedInterfaces.Add(netInterface);
    }

    internal bool TryEncryptPacket(ByteArrayPool.MemoryOwner plain, ulong gene, out ByteArrayPool.MemoryOwner encrypted)
    {
        if (this.embryo == null || plain.Memory.Length < PacketService.HeaderSize)
        {
            encrypted = default;
            return false;
        }

        var span = plain.Memory.Span;
        var source = span.Slice(PacketService.HeaderSize);
        Span<byte> iv = stackalloc byte[16];
        BitConverter.TryWriteBytes(iv, gene);
        this.embryo.AsSpan(32, 8).CopyTo(iv.Slice(8));

        var packet = PacketPool.Rent();
        span.Slice(0, PacketService.HeaderSize).CopyTo(packet.ByteArray);

        this.aes!.TryEncryptCbc(source, iv, packet.ByteArray.AsSpan(PacketService.HeaderSize), out var written, PaddingMode.PKCS7);
        encrypted = packet.ToMemoryOwner(0, PacketService.HeaderSize + written);
        PacketService.InsertDataSize(encrypted.Memory, (ushort)written);

        return true;
    }

    internal bool TryDecryptPacket(ByteArrayPool.MemoryOwner encrypted, ulong gene, out ByteArrayPool.MemoryOwner destination)
    {
        if (this.embryo == null)
        {
            destination = default;
            return false;
        }

        Span<byte> iv = stackalloc byte[16];
        BitConverter.TryWriteBytes(iv, gene);
        this.embryo.AsSpan(32, 8).CopyTo(iv.Slice(8));

        var packet = PacketPool.Rent();
        this.aes!.TryDecryptCbc(encrypted.Memory.Span, iv, packet.ByteArray, out var written, PaddingMode.PKCS7);
        destination = packet.ToMemoryOwner(0, written);

        return true;
    }

    internal void ResetLastResponseMics() => this.lastResponseMics = Mics.GetSystem();

    internal void SetLastResponseMics(long mics) => this.lastResponseMics = mics;

    internal long LastResponseMics => this.lastResponseMics;

    internal GenePool? TryFork() => this.embryo == null ? null : this.GenePool.Fork(this.embryo);

    internal void IncrementResendCount() => Interlocked.Increment(ref this.resendCount);

    internal uint ResendCount => Volatile.Read(ref this.resendCount);

    private void Clear()
    {// lock (this.SyncObject)
        foreach (var x in this.activeInterfaces)
        {
            x.Clear();
        }

        this.activeInterfaces.Clear();

        foreach (var x in this.disposedInterfaces)
        {
            x.Clear();
        }

        this.disposedInterfaces.Clear();
    }

    private void InitializeState()
    {
        this.SetMaximumResponseTime();
        this.SetMinimumBandwidth();
        this.ResetLastResponseMics();
    }

    protected List<NetInterface> activeInterfaces = new();
    protected List<NetInterface> disposedInterfaces = new();
    protected byte[]? embryo; // 48 bytes
    private Aes? aes;

    private long maximumResponseMics;
    private double minimumBandwidth;
    private long lastSendingAckMics;
    private long lastResponseMics;

    private uint resendCount;

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
                if (this.IsEncrypted && !this.IsClosed)
                {// Close connection.
                    this.SendClose().Wait(); // temporary
                }

                this.IsClosed = true;
                this.Terminal.TryRemove(this);
                lock (this.SyncObject)
                {
                    this.Clear();
                }

                this.ConnectionSemaphore.Dispose();

                // this.TerminalLogger?.Information("terminal disposed.");
            }

            // free native resources here if there are any.
            this.disposed = true;
        }
    }
    #endregion
}
