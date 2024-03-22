﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Netsphere.Net;

namespace Netsphere;

#pragma warning disable SA1202 // Elements should be ordered by access

public class ReceiveStream // : IDisposable
{
    internal ReceiveStream(ReceiveTransmission receiveTransmission, ulong dataId, long maxStreamLength)
    {
        this.ReceiveTransmission = receiveTransmission;
        this.DataId = dataId;
        this.MaxStreamLength = maxStreamLength;
    }

    #region FieldAndProperty

    internal ReceiveTransmission ReceiveTransmission { get; }

    public ulong DataId { get; }

    public long MaxStreamLength { get; internal set; }

    public long ReceivedLength { get; internal set; }

    internal int CurrentGene { get; set; }

    internal int CurrentPosition { get; set; }

    #endregion

    public void Cancel()
    {
        this.DisposeImmediately();
    }

    /*public void Dispose()
    {
        this.DisposeImmediately();
    }*/

    public async Task<(NetResult Result, int Written)> Receive(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var r = await this.ReceiveTransmission.ProcessReceive(this, buffer, cancellationToken).ConfigureAwait(false);
        if (r.Result != NetResult.Success)
        {
            if (r.Result == NetResult.Completed ||
                r.Result == NetResult.Canceled)
            {
                Debug.Assert(this.ReceiveTransmission.Mode == NetTransmissionMode.Disposed);
            }
            else
            {
                this.Cancel();
            }
        }

        return r;
    }

    public async Task<NetResultValue<TReceive>> ReceiveBlock<TReceive>(CancellationToken cancellationToken = default)
    {
        var buffer = NetHelper.RentBuffer();
        try
        {
            var (result, written) = await this.Receive(buffer.AsMemory(0, sizeof(int)), cancellationToken).ConfigureAwait(false);
            if (result != NetResult.Success)
            {
                return new(result);
            }
            else if (written != sizeof(int))
            {
                this.Cancel();
                return new(NetResult.DeserializationFailed);
            }

            var length = BitConverter.ToInt32(buffer);
            if (length > this.ReceiveTransmission.Connection.Agreement.MaxBlockSize)
            {
                this.Cancel();
                return new(NetResult.BlockSizeLimit);
            }

            var memory = buffer.AsMemory(sizeof(int));
            if (memory.Length > length)
            {
                memory = memory.Slice(0, length);
            }
            else
            {
                memory = new byte[length];
            }

            (result, written) = await this.Receive(memory, cancellationToken).ConfigureAwait(false);
            if (result != NetResult.Success)
            {
                return new(result);
            }
            else if (written != length)
            {
                this.Cancel();
                return new(NetResult.DeserializationFailed);
            }

            if (!TinyhandSerializer.TryDeserialize<TReceive>(memory.Span, out var value))
            {
                this.Cancel();
                return new(NetResult.DeserializationFailed);
            }

            return new(NetResult.Success, value);
        }
        finally
        {
            NetHelper.ReturnBuffer(buffer);
        }
    }

    internal void DisposeImmediately()
    {
        this.ReceiveTransmission.ProcessDispose();
    }
}
