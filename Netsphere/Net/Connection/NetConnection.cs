﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

internal readonly record struct Embryo
{
    public readonly ulong Salt;
    public readonly ulong Salt2;
    public readonly byte[] Key; // 32 bytes
    public readonly byte[] Iv; // 16 bytes
}

public class NetConnection : IDisposable
{
    public enum ConnectMode
    {
        ReuseClosed,
        ReuseOpen,
        NoReuse,
    }

    public NetConnection(ulong connectionId, NetEndPoint endPoint)
    {
        this.ConnectionId = connectionId;
        this.EndPoint = endPoint;
    }

    #region FieldAndProperty

    public ulong ConnectionId { get; }

    public NetEndPoint EndPoint { get; }

    internal long ClosedSystemMics { get; set; }

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
            }

            // free native resources here if there are any.
            this.disposed = true;
        }
    }
    #endregion
}
