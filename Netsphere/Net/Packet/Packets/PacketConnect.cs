﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Netsphere.Packet;

[TinyhandObject]
internal partial class PacketConnect : IPacket
{
    public static PacketType PacketType => PacketType.Connect;

    public PacketConnect()
    {
    }

    public PacketConnect(ushort engagement, NodePublicKey clientPublicKey, int serverPublicKeyChecksum)
    {
        this.Engagement = engagement;
        this.ClientPublicKey = clientPublicKey;
        this.ServerPublicKeyChecksum = serverPublicKeyChecksum;
        this.ClientSalt = RandomVault.Crypto.NextUInt64();
        this.ClientSalt2 = RandomVault.Crypto.NextUInt64();
    }

    [Key(0)]
    public uint NetIdentification { get; set; }

    [Key(1)]
    public ushort Engagement { get; set; }

    [Key(2)]
    public NodePublicKey ClientPublicKey { get; set; }

    [Key(3)]
    public int ServerPublicKeyChecksum { get; set; }

    [Key(4)]
    public ulong ClientSalt { get; set; }

    [Key(5)]
    public ulong ClientSalt2 { get; set; }

    [Key(6)]
    public bool Bidirectional { get; set; }
}

[TinyhandObject]
internal partial class PacketConnectResponse : IPacket
{
    public static PacketType PacketType => PacketType.ConnectResponse;

    public PacketConnectResponse()
    {
        this.Agreement = ConnectionAgreement.Default;
        this.ServerSalt = RandomVault.Crypto.NextUInt64();
        this.ServerSalt2 = RandomVault.Crypto.NextUInt64();
    }

    public PacketConnectResponse(ConnectionAgreement agreement)
        : this()
    {
        // this.Success = true;
        this.Agreement = agreement;
    }

    /*[Key(0)]
    public bool Success { get; set; }*/

    [Key(1)]
    public ulong ServerSalt { get; set; }

    [Key(2)]
    public ulong ServerSalt2 { get; set; }

    [Key(3)]
    public ConnectionAgreement Agreement { get; set; }
}
