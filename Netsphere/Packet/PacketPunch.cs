// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Netsphere;

[TinyhandObject]
public partial class PacketPunch : IPacket
{
    public PacketId PacketId => PacketId.Punch;

    public bool AllowUnencrypted => true;

    public PacketPunch()
    {
    }

    public PacketPunch(IPEndPoint? nextEndpoint)
    {
        this.UtcMics = Mics.GetUtcNow();
        this.NextEndpoint = nextEndpoint;
    }

    [Key(0)]
    public long UtcMics { get; set; }

    [Key(1)]
    public bool Relay { get; set; } // Relay this packet to the next endpoint (NextEndpoint must be a valid value).

    [Key(2)]
    public IPEndPoint? NextEndpoint { get; set; }
}

[TinyhandObject]
public partial class PacketPunchResponse : IPacket
{
    public PacketId PacketId => PacketId.PunchResponse;

    public bool AllowUnencrypted => true;

    [Key(0)]
    public long UtcMics { get; set; }

    [Key(1)]
    public IPEndPoint Endpoint { get; set; } = default!;

    public override string ToString() => $"{this.Endpoint}";
}
