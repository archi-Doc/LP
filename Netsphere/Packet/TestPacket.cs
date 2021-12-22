﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

[TinyhandObject]
public partial class TestPacket : IPacket
{
    public const uint DataMax = 4_000_000;

    public static TestPacket Create(uint size = DataMax)
    {
        size = size < DataMax ? size : DataMax;

        var testBlock = new TestPacket();
        testBlock.N = 10;
        testBlock.Message = "Test message";
        testBlock.Data = new byte[size];
        for (var n = 0; n < testBlock.Data.Length; n++)
        {
            testBlock.Data[n] = (byte)n;
        }

        return testBlock;
    }

    [Key(0)]
    public int N { get; set; }

    [Key(1)]
    public string Message { get; set; } = string.Empty;

    [Key(2)]
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public uint BlockId => 0xd75af226;

    public PacketId PacketId => PacketId.Test;

    public override string ToString()
        => $"TestPacket: {this.N}, {this.Message}, Size:{this.Data.Length}, Hash:{Arc.Crypto.FarmHash.Hash64(this.Data).To4Hex()}";
}
