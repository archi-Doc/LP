﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

[TinyhandObject]
public readonly partial record struct NetEndpoint : IEquatable<NetEndpoint>
{
    public NetEndpoint(ushort relayId, IPEndPoint? endPoint)
    {
        this.EndPoint = endPoint;
        this.RelayId = relayId;
    }

    [Key(0)]
    public readonly ushort RelayId;

    [Key(1)]
    public readonly IPEndPoint? EndPoint;

    public bool IsValid
        => this.EndPoint is not null;

    /*public NetAddress ToNetAddress()
        => new NetAddress(this.EndPoint.Address, (ushort)this.EndPoint.Port);*/

    public bool IsPrivateOrLocalLoopbackAddress()
        => this.EndPoint is not null &&
        new NetAddress(this.EndPoint.Address, (ushort)this.EndPoint.Port).IsPrivateOrLocalLoopbackAddress();

    public bool EndPointEquals(NetEndpoint endpoint)
    {
        if (this.EndPoint is null)
        {
            return endpoint.EndPoint is null;
        }
        else
        {
            return this.EndPoint.Equals(endpoint.EndPoint);
        }
    }

    public bool Equals(NetEndpoint endPoint)
        => this.RelayId == endPoint.RelayId &&
        this.EndPoint?.Equals(endPoint.EndPoint) == true;

    public override int GetHashCode()
        => HashCode.Combine(this.RelayId, this.EndPoint);

    public override string ToString()
        => $"[{this.RelayId.ToString()}]{this.EndPoint?.ToString()}";
}
