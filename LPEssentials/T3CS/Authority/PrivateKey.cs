﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LP;

#pragma warning disable SA1214

[TinyhandObject]
public sealed partial class PrivateKey : IValidatable, IEquatable<PrivateKey>
{
    public const int MaxNameLength = 16;
    private const int MaxPrivateKeyCache = 10;

    private static ObjectCache<PrivateKey, ECDsa> PrivateKeyToECDsa { get; } = new(MaxPrivateKeyCache);

    public static PrivateKey Create()
    {
        using (var ecdsa = ECDsa.Create(PublicKey.ECCurve))
        {
            var key = ecdsa.ExportParameters(true);
            return new PrivateKey(0, key.Q.X!, key.Q.Y!, key.D!);
        }
    }

    public static PrivateKey Create(ReadOnlySpan<byte> seed)
    {
        ECParameters key = default;
        key.Curve = PublicKey.ECCurve;

        byte[]? d = null;
        var hash = Hash.ObjectPool.Get();
        while (true)
        {
            try
            {
                if (d == null)
                {
                    d = hash.GetHash(seed);
                }
                else
                {
                    d = hash.GetHash(d);
                }

                key.D = d;
                using (var ecdsa = ECDsa.Create(key))
                {
                    key = ecdsa.ExportParameters(true);
                    break;
                }
            }
            catch
            {
            }
        }

        Hash.ObjectPool.Return(hash);
        return new PrivateKey(0, key.Q.X!, key.Q.Y!, key.D!);
    }

    public static PrivateKey Create(string passphrase)
    {
        ECParameters key = default;
        key.Curve = PublicKey.ECCurve;

        var passBytes = Encoding.UTF8.GetBytes(passphrase);
        Span<byte> span = stackalloc byte[(sizeof(ulong) + passBytes.Length) * 2]; // count, passBytes, count, passBytes // scoped
        var countSpan = span.Slice(0, sizeof(ulong));
        var countSpan2 = span.Slice(sizeof(ulong) + passBytes.Length, sizeof(ulong));
        passBytes.CopyTo(span.Slice(sizeof(ulong)));
        passBytes.CopyTo(span.Slice((sizeof(ulong) * 2) + passBytes.Length));

        return Create(span);
    }

    internal PrivateKey()
    {
    }

    private PrivateKey(byte keyType, byte[] x, byte[] y, byte[] d)
    {
        this.x = x;
        this.y = y;
        this.d = d;

        var yTilde = this.CompressY();
        this.rawType = (byte)(((keyType << 2) & ~3) + (yTilde & 1));
    }

    public byte[]? SignData(ReadOnlySpan<byte> data)
    {
        var ecdsa = PrivateKeyToECDsa.TryGet(this) ?? this.TryCreateECDsa();
        if (ecdsa == null)
        {
            return null;
        }

        var sign = new byte[PublicKey.SignLength];
        if (!ecdsa.TrySignData(data, sign.AsSpan(), PublicKey.HashAlgorithmName, out var written))
        {
            return null;
        }

        PrivateKeyToECDsa.Cache(this, ecdsa);
        return sign;
    }

    public bool SignData(ReadOnlySpan<byte> data, Span<byte> signature)
    {
        if (signature.Length < PublicKey.SignLength)
        {
            return false;
        }

        var ecdsa = PrivateKeyToECDsa.TryGet(this) ?? this.TryCreateECDsa();
        if (ecdsa == null)
        {
            return false;
        }

        if (!ecdsa.TrySignData(data, signature, PublicKey.HashAlgorithmName, out var written))
        {
            return false;
        }

        PrivateKeyToECDsa.Cache(this, ecdsa);
        return true;
    }

    public bool VerifyData(ReadOnlySpan<byte> data, ReadOnlySpan<byte> sign)
    {
        var publicKey = new PublicKey(this);
        return publicKey.VerifyData(data, sign);
    }

    public ECDsa? TryCreateECDsa()
    {
        if (!this.Validate())
        {
            return null;
        }

        if (this.KeyType == 0)
        {
            try
            {
                ECParameters p = default;
                p.Curve = PublicKey.ECCurve;
                p.D = this.d;
                return ECDsa.Create(p);
            }
            catch
            {
            }
        }

        return null;
    }

    [Key(0)]
    private readonly byte rawType; // 6bits: KeyType, 1bit:?, 1bit: YTilde

    [Key(1)]
    private readonly byte[] x = Array.Empty<byte>();

    [Key(2)]
    private readonly byte[] y = Array.Empty<byte>();

    [Key(3)]
    private readonly byte[] d = Array.Empty<byte>();

    public uint KeyType => (uint)(this.rawType >> 2);

    public uint YTilde => (uint)(this.rawType & 1);

    public byte[] X => this.x;

    public byte[] Y => this.y;

    public bool Validate()
    {
        if (this.KeyType != 0)
        {
            return false;
        }
        else if (this.x == null || this.x.Length != PublicKey.PublicKeyHalfLength)
        {
            return false;
        }
        else if (this.y == null || this.y.Length != PublicKey.PublicKeyHalfLength)
        {
            return false;
        }
        else if (this.d == null || this.d.Length != PublicKey.PrivateKeyLength)
        {
            return false;
        }

        return true;
    }

    public bool Equals(PrivateKey? other)
    {
        if (other == null)
        {
            return false;
        }

        return this.rawType == other.rawType &&
            this.x.AsSpan().SequenceEqual(other.x);
    }

    public override int GetHashCode()
    {
        var hash = HashCode.Combine(this.rawType);

        if (this.x.Length >= sizeof(ulong))
        {
            hash ^= BitConverter.ToInt32(this.x, 0);
        }

        return hash;
    }

    public override string ToString()
    {
        Span<byte> bytes = stackalloc byte[1 + PublicKey.PublicKeyHalfLength]; // scoped
        bytes[0] = this.rawType;
        this.x.CopyTo(bytes.Slice(1));
        return $"({Base64.Url.FromByteArrayToString(bytes)})";
    }

    internal uint CompressY()
        => Arc.Crypto.EC.P256R1Curve.Instance.CompressY(this.y);

    internal byte RawType => this.rawType;
}
