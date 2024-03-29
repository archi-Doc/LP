﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Core;
using Netsphere.Crypto;
using Netsphere.Packet;
using Netsphere.Responder;
using Netsphere.Stats;

namespace Netsphere;

public class NetTerminal : UnitBase, IUnitPreparable, IUnitExecutable
{
    public enum State
    {
        Initial,
        Active,
        Shutdown,
    }

    public NetTerminal(UnitContext unitContext, UnitLogger unitLogger, NetBase netBase, NetStats netStats)
        : base(unitContext)
    {
        this.UnitLogger = unitLogger;
        this.NetBase = netBase;
        this.NetStats = netStats;

        this.NetSender = new(this, this.NetBase, unitLogger.GetLogger<NetSender>());
        this.PacketTerminal = new(this.NetBase, this.NetStats, this, unitLogger.GetLogger<PacketTerminal>());
        this.ConnectionTerminal = new(unitContext.ServiceProvider, this);
        this.netCleaner = new(this);

        this.ConnectTimeout = NetConstants.DefaultConnectTimeout;
    }

    #region FieldAndProperty

    public State CurrentState { get; private set; }

    public bool IsActive => this.CurrentState == State.Active;

    public NetBase NetBase { get; }

    public string NetTerminalString => this.IsAlternative ? "Alt" : "Main";

    public NodePublicKey NodePublicKey { get; private set; }

    public NetStats NetStats { get; }

    public ResponderControl Responders { get; private set; } = default!;

    public ServiceControl Services { get; private set; } = default!;

    public PacketTerminal PacketTerminal { get; }

    public bool IsAlternative { get; private set; }

    public int Port { get; set; }

    public TimeSpan ConnectTimeout { get; set; }

    internal NodePrivateKey NodePrivateKey { get; private set; } = default!;

    internal NetSender NetSender { get; }

    internal UnitLogger UnitLogger { get; private set; }

    internal ConnectionTerminal ConnectionTerminal { get; private set; }

    private readonly NetCleaner netCleaner;

    #endregion

    public void Clean()
    {
        this.ConnectionTerminal.Clean();
    }

    public bool TryCreateEndPoint(in NetAddress address, out NetEndPoint endPoint)
        => this.NetStats.TryCreateEndPoint(in address, out endPoint);

    public void SetDeliveryFailureRatioForTest(double ratio)
    {
        this.NetSender.SetDeliveryFailureRatio(ratio);
    }

    public void SetReceiveTransmissionGapForTest(uint gap)
    {
        this.ConnectionTerminal.SetReceiveTransmissionGapForTest(gap);
    }

    public async Task<NetNode?> UnsafeGetNetNode(NetAddress address)
    {
        if (!this.NetBase.AllowUnsafeConnection)
        {
            return null;
        }

        var t = await this.PacketTerminal.SendAndReceive<GetInformationPacket, GetInformationPacketResponse>(address, new()).ConfigureAwait(false);
        if (t.Value is null)
        {
            return null;
        }

        return new(address, t.Value.PublicKey);
    }

    public Task<ClientConnection?> Connect(NetNode destination, Connection.ConnectMode mode = Connection.ConnectMode.ReuseIfAvailable)
        => this.ConnectionTerminal.Connect(destination, mode);

    public async Task<ClientConnection?> UnsafeConnect(NetAddress destination, Connection.ConnectMode mode = Connection.ConnectMode.ReuseIfAvailable)
    {
        var netNode = await this.UnsafeGetNetNode(destination).ConfigureAwait(false);
        if (netNode is null)
        {
            return default;
        }

        return await this.Connect(netNode, mode).ConfigureAwait(false);
    }

    public void SetNodeKey(NodePrivateKey nodePrivateKey)
    {
        this.NodePrivateKey = nodePrivateKey;
        this.NodePublicKey = nodePrivateKey.ToPublicKey();
    }

    void IUnitPreparable.Prepare(UnitMessage.Prepare message)
    {
        if (this.Port == 0)
        {
            this.Port = this.NetBase.NetOptions.Port;
        }

        if (!this.IsAlternative)
        {
            this.NodePrivateKey = this.NetBase.NodePrivateKey;
        }
        else
        {
            this.NodePrivateKey = NodePrivateKey.AlternativePrivateKey;
            this.Port = NetAddress.Alternative.Port;
        }

        this.NodePublicKey = this.NodePrivateKey.ToPublicKey();
    }

    async Task IUnitExecutable.StartAsync(UnitMessage.StartAsync message, CancellationToken cancellationToken)
    {
        this.CurrentState = State.Active;

        var core = message.ParentCore;
        await this.NetSender.StartAsync(core);
        this.netCleaner.Start(core);
    }

    void IUnitExecutable.Stop(UnitMessage.Stop message)
    {
    }

    async Task IUnitExecutable.TerminateAsync(UnitMessage.TerminateAsync message, CancellationToken cancellationToken)
    {
        // Close all connections
        this.CurrentState = State.Shutdown;

        await this.ConnectionTerminal.Terminate(cancellationToken).ConfigureAwait(false);

        this.NetSender.Stop();
        this.netCleaner.Stop();
    }

    internal void Initialize(ResponderControl responders, ServiceControl services, bool isAlternative)
    {
        this.Responders = responders;
        this.Services = services;
        this.IsAlternative = isAlternative;
    }

    internal async Task<NetResponse> Wait(Task<NetResponse> task, TimeSpan timeout, CancellationToken cancellationToken)
    {// I don't think this is a smart approach, but...
        var remaining = timeout;
        while (true)
        {
            if (!this.IsActive)
            {// NetTerminal
                return new(NetResult.Closed);
            }

            try
            {
                var result = await task.WaitAsync(NetConstants.WaitIntervalTimeSpan, cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (TimeoutException)
            {
                if (remaining < TimeSpan.Zero)
                {// Wait indefinitely.
                }
                else if (remaining > NetConstants.WaitIntervalTimeSpan)
                {// Reduce the time and continue waiting.
                    remaining -= NetConstants.WaitIntervalTimeSpan;
                }
                else
                {// Timeout
                    return new(NetResult.Timeout);
                }
            }
        }
    }

    internal void ProcessSend(NetSender netSender)
    {
        // 1st: PacketTerminal (Packets: Connect, Ack, Loss, ...)
        this.PacketTerminal.ProcessSend(netSender);
        if (!netSender.CanSend)
        {
            return;
        }

        // 2nd: AckBuffer (Ack)
        this.ConnectionTerminal.AckQueue.ProcessSend(netSender);
        if (!netSender.CanSend)
        {
            return;
        }

        // 3rd: ConnectionTerminal (SendTransmission/SendGene)
        this.ConnectionTerminal.ProcessSend(netSender);
    }

    internal unsafe void ProcessReceive(IPEndPoint endPoint, ByteArrayPool.Owner toBeShared, int packetSize)
    {
        var currentSystemMics = Mics.FastSystem;
        var owner = toBeShared.ToMemoryOwner(0, packetSize);
        var span = owner.Span;

        if (packetSize < PacketHeader.Length)
        {// Check length
            return;
        }

        // Engagement
        span = span.Slice(4);
        var engagement = BitConverter.ToUInt16(span);

        // Packet type
        span = span.Slice(2);
        var packetType = BitConverter.ToUInt16(span);

        if (packetType < 256)
        {// Packet
            this.PacketTerminal.ProcessReceive(endPoint, packetType, owner, currentSystemMics);
        }
        else if (packetType < 511)
        {// Gene
            this.ConnectionTerminal.ProcessReceive(endPoint, packetType, owner, currentSystemMics);
        }
    }
}
