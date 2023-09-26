﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

#pragma warning disable SA1202

namespace LP.T3CS;

/// <summary>
/// Represents a public key data. Compressed to 33 bytes (memory usage 40 bytes).
/// </summary>
[TinyhandObject]
public readonly partial struct PublicKey : IValidatable, IEquatable<PublicKey>
{
    #region Unique

    private static ObjectCache<PublicKey, ECDsa> EcdsaCache { get; } = new(100);

    public bool VerifyData(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != KeyHelper.SignatureLength)
        {
            return false;
        }

        var ecdsa = this.TryGetEcdsa();
        if (ecdsa == null)
        {
            return false;
        }

        Span<byte> hash = stackalloc byte[32];
        Sha3Helper.Get256_Span(data, hash);
        var result = ecdsa.VerifyHash(hash, signature);
        EcdsaCache.Cache(this, ecdsa);
        return result;
    }

    public unsafe bool VerifyIdentifier(Identifier identifier, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != KeyHelper.SignatureLength)
        {
            return false;
        }

        var ecdsa = this.TryGetEcdsa();
        if (ecdsa == null)
        {
            return false;
        }

        var result = ecdsa.VerifyHash(new ReadOnlySpan<byte>(Unsafe.AsPointer(ref identifier), sizeof(Identifier)), signature);
        EcdsaCache.Cache(this, ecdsa);
        return result;
    }

    public ECDiffieHellman? TryGetEcdh()
    {
        var x = new byte[32];
        this.WriteX(x);
        return KeyHelper.CreateEcdhFromX(x, this.YTilde);
    }

    private ECDsa? TryGetEcdsa()
    {
        if (EcdsaCache.TryGet(this) is { } ecdsa)
        {
            return ecdsa;
        }

        var x = new byte[32];
        this.WriteX(x);
        return KeyHelper.CreateEcdsaFromX(x, this.YTilde);
    }

    #endregion

    #region TypeSpecific

    public static bool TryParse(ReadOnlySpan<char> chars, [MaybeNullWhen(false)] out PublicKey publicKey)
    {
        if (KeyHelper.TryParsePublicKey(chars, out var keyValue, out var x) &&
            KeyHelper.GetKeyClass(keyValue) == KeyClass.T3CS_Signature)
        {
            publicKey = new(keyValue, x);
            return true;
        }

        publicKey = default;
        return false;
    }

    public PublicKey()
    {
    }

    internal PublicKey(byte keyValue, ReadOnlySpan<byte> x)
    {
        this.keyValue = KeyHelper.ToPublicKeyValue(keyValue);
        var b = x;
        this.x0 = BitConverter.ToUInt64(b);
        b = b.Slice(sizeof(ulong));
        this.x1 = BitConverter.ToUInt64(b);
        b = b.Slice(sizeof(ulong));
        this.x2 = BitConverter.ToUInt64(b);
        b = b.Slice(sizeof(ulong));
        this.x3 = BitConverter.ToUInt64(b);
    }

    public bool IsSameKey(PrivateKey privateKey)
    {
        if (KeyHelper.ToPublicKeyValue(privateKey.KeyValue) != this.KeyValue)
        {
            return false;
        }

        var span = privateKey.X.AsSpan();
        if (span.Length != KeyHelper.PublicKeyHalfLength)
        {
            return false;
        }

        if (this.x0 != BitConverter.ToUInt64(span))
        {
            return false;
        }

        span = span.Slice(sizeof(ulong));
        if (this.x1 != BitConverter.ToUInt64(span))
        {
            return false;
        }

        span = span.Slice(sizeof(ulong));
        if (this.x2 != BitConverter.ToUInt64(span))
        {
            return false;
        }

        span = span.Slice(sizeof(ulong));
        if (this.x3 != BitConverter.ToUInt64(span))
        {
            return false;
        }

        return true;
    }

    public bool Validate()
        => this.KeyClass == KeyClass.T3CS_Signature &&
            this.x0 != 0 && this.x1 != 0 && this.x2 != 0 && this.x3 != 0;

    public bool Equals(PublicKey other)
        => this.keyValue == other.keyValue &&
        this.x0 == other.x0 && this.x1 == other.x1 && this.x2 == other.x2 && this.x3 == other.x3;

    public override string ToString()
        => $"({this.ToBase64()})";

    #endregion

    #region Common

    [Key(0)]
    private readonly byte keyValue;

    [Key(1)]
    private readonly ulong x0;

    [Key(2)]
    private readonly ulong x1;

    [Key(3)]
    private readonly ulong x2;

    [Key(4)]
    private readonly ulong x3;

    public byte KeyValue => this.keyValue;

    public KeyClass KeyClass => KeyHelper.GetKeyClass(this.keyValue);

    public uint YTilde => KeyHelper.GetYTilde(this.keyValue);

    public bool TryWriteBytes(Span<byte> destination, out int written)
    {
        if (destination.Length < KeyHelper.EncodedLength)
        {
            written = 0;
            return false;
        }

        var b = destination;
        b[0] = this.keyValue;
        b = b.Slice(1);
        this.WriteX(b);

        written = KeyHelper.EncodedLength;
        return true;
    }

    public void WriteX(Span<byte> span)
    {
        var b = span;
        BitConverter.TryWriteBytes(b, this.x0);
        b = b.Slice(sizeof(ulong));
        BitConverter.TryWriteBytes(b, this.x1);
        b = b.Slice(sizeof(ulong));
        BitConverter.TryWriteBytes(b, this.x2);
        b = b.Slice(sizeof(ulong));
        BitConverter.TryWriteBytes(b, this.x3);
    }

    public ulong GetChecksum()
    {
        Span<byte> span = stackalloc byte[KeyHelper.EncodedLength];
        this.TryWriteBytes(span, out _);
        return FarmHash.Hash64(span);
    }

    public string ToBase64()
    {
        Span<byte> span = stackalloc byte[KeyHelper.EncodedLength];
        this.TryWriteBytes(span, out _);
        return $"{Base64.Url.FromByteArrayToString(span)}";
    }

    public Identifier ToIdentifier()
        => new(this.x0, this.x1, this.x2, this.x3);

    public override int GetHashCode()
        => (int)this.x0;

    #endregion
}