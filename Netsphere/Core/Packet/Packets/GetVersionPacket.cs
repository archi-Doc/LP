﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Netsphere.Crypto;
using Netsphere.Packet;
using Netsphere.Version;

namespace Netsphere.Relay;

[TinyhandObject]
public sealed partial class GetVersionPacket : IPacket
{
    public static PacketType PacketType => PacketType.GetVersion;

    public GetVersionPacket()
    {
    }
}

[TinyhandObject]
public sealed partial class GetVersionResponse : IPacket
{
    public static PacketType PacketType => PacketType.GetVersionResponse;

    public GetVersionResponse()
    {
    }

    [Key(0)]
    public CertificateToken<VersionInfo>? Token { get; set; }
}
