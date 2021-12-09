﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Netsphere;

[TinyhandObject]
internal partial class PacketData : IPacket
{
    public PacketId PacketId => PacketId.Data;

    // DataHeader
    // byte[DataSize] Data
}

[StructLayout(LayoutKind.Explicit)]
internal readonly struct DataHeader
{
    public static ulong ChecksumMask = 0xffffffffffffff00ul;

    public DataHeader(ulong dataId, PacketId packetId, ulong checksum)
    {
        this.DataId = dataId;
        this.PacketId = packetId;
        this.Checksum = (checksum & ChecksumMask) | (byte)packetId;
    }

    public bool ChecksumEquals(ulong checksum)
        => (checksum & ChecksumMask) == (this.Checksum & ChecksumMask);

    [FieldOffset(0)]
    public readonly ulong DataId;

    [FieldOffset(8)]
    public readonly PacketId PacketId;

    [FieldOffset(8)]
    public readonly ulong Checksum; // 1-7bytes, FarmHash64
}
