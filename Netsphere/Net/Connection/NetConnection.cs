﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Packet;

namespace Netsphere;

// byte[32] Key, byte[16] Iv
internal readonly record struct Embryo(ulong Salt, byte[] Key, byte[] Iv);

public class NetConnection : IDisposable
{
    public enum ConnectMode
    {
        ReuseClosed,
        ReuseOpen,
        NoReuse,
    }

    public NetConnection(NetTerminal netTerminal, ulong connectionId, NetEndPoint endPoint)
    {
        this.netTerminal = netTerminal;
        this.ConnectionId = connectionId;
        this.EndPoint = endPoint;
    }

    public void Close()
        => this.Dispose();

    internal void SetEmbryo(Embryo embryo)
        => this.embryo = embryo;

    #region FieldAndProperty

    public ulong ConnectionId { get; }

    public NetEndPoint EndPoint { get; }

    internal long ClosedSystemMics { get; set; }

    private readonly NetTerminal netTerminal;
    private Embryo embryo;

    #endregion

#pragma warning disable SA1124 // Do not use regions
    #region IDisposable Support
#pragma warning restore SA1124 // Do not use regions

    private bool disposed = false; // To detect redundant calls.

    /// <summary>
    /// Finalizes an instance of the <see cref="NetConnection"/> class.
    /// </summary>
    ~NetConnection()
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
                this.netTerminal.PacketTerminal.SendAndForget(this.EndPoint, new PacketClose());
            }

            // free native resources here if there are any.
            this.disposed = true;
        }
    }
    #endregion
}
