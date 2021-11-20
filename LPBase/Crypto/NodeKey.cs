﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Security.Cryptography;

namespace LP;

public static class NodeKey
{
    public const string ECCurveName = "secp256r1";
    public const int PublicKeySize = 64;
    public const int PrivateKeySize = 32;
    public const string Filename = "Node.key";

    public static ECCurve ECCurve { get; }

    static NodeKey()
    {
        ECCurve = ECCurve.CreateFromFriendlyName(ECCurveName);
    }

    public static ECDiffieHellman? FromPrivateKey(NodePrivateKey key)
    {
        try
        {
            ECParameters p = default;
            p.Curve = ECCurve;
            p.D = key.D;
            return ECDiffieHellman.Create(p);
        }
        catch
        {
            return null;
        }
    }

    public static ECDiffieHellman? FromPublicKey(byte[] x, byte[] y)
    {
        try
        {
            ECParameters p = default;
            p.Curve = ECCurve;
            p.Q.X = x;
            p.Q.Y = y;
            return ECDiffieHellman.Create(p);
        }
        catch
        {
            return null;
        }
    }
}

[TinyhandObject]
public partial class NodePrivateKey
{
    public const string ECCurveName = "secp256r1";
    public const string Filename = "Node.key";

    public static NodePrivateKey AlternativePrivateKey
    {
        get
        {
            if (alternativePrivateKey == null)
            {
                alternativePrivateKey = NodePrivateKey.Create("Alternative");
            }

            return alternativePrivateKey;
        }
    }

    public static NodePrivateKey Create(string? name = null)
    {
        var curve = ECCurve.CreateFromFriendlyName(ECCurveName);
        var ecdh = ECDiffieHellman.Create(curve);
        var key = ecdh.ExportParameters(true);

        return new NodePrivateKey(name, key.D!, key.Q.X!, key.Q.Y!);
    }

    public NodePrivateKey()
    {
    }

    public NodePrivateKey(string? name, byte[] d, byte[] x, byte[] y)
    {
        this.Name = name ?? string.Empty;
        this.D = d;
        this.X = x;
        this.Y = y;
    }

    [Key(0)]
    public string Name { get; set; } = string.Empty;

    [Key(1)]
    public byte[] D { get; set; } = default!;

    [Key(2)]
    public byte[] X { get; set; } = default!;

    [Key(3)]
    public byte[] Y { get; set; } = default!;

    private static NodePrivateKey? alternativePrivateKey;
}

[TinyhandObject]
public partial class NodePublicKey
{
    public NodePublicKey()
    {
    }

    public NodePublicKey(NodePrivateKey privateKey)
    {
        this.X = privateKey.X;
        this.Y = privateKey.Y;
    }

    [Key(0)]
    public byte[] X { get; set; } = default!;

    [Key(1)]
    public byte[] Y { get; set; } = default!;
}
