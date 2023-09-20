﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Tinyhand.IO;
using Tinyhand.Tree;

namespace LP.T3CS;

/// <summary>
/// Represents a linkage key.
/// </summary>
[TinyhandObject]
public readonly partial struct LinkageKey // : IValidatable, IEquatable<LinkageKey>
{
    public const int EncodedLength = PublicKey.EncodedLength + sizeof(uint) + sizeof(byte) + (sizeof(ulong) * 4);

    public static LinkageKey CreateRaw(PublicKey publicKey)
        => new(publicKey);

    public static LinkageKey CreateEncrypted(PublicKey publicKey, PublicKey encryptionKey)
    {
        Span<byte> destination = stackalloc byte[32];

        var newKey = PrivateKey.CreateEncryptionKey();
        using (var ecdh = newKey.TryGetEcdh())
        using (var ecdh2 = encryptionKey.TryGetEcdh())
        {
            if (ecdh is null || ecdh2 is null)
            {
                throw new InvalidOperationException();
            }

            var material = ecdh.DeriveKeyMaterial(ecdh2.PublicKey);

            // Hash key material
            Sha3Helper.Get256_Span(material, material);

            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Key = material;

                Span<byte> source = stackalloc byte[32];
                Span<byte> iv = stackalloc byte[16];
                publicKey.WriteX(source);
                aes.TryEncryptCbc(source, iv, destination, out _, PaddingMode.None);
            }
        }

        return new(publicKey, newKey, destination);
    }

    public LinkageKey()
    {
    }

    private LinkageKey(PublicKey publicKey)
    {// Raw
        this.Key = publicKey;
        this.Checksum = (uint)publicKey.GetChecksum();
    }

    private LinkageKey(PublicKey publicKey, PrivateKey newKey, ReadOnlySpan<byte> encrypted)
    {// Encrypted
        this.Key = newKey.ToPublicKey();
        this.Checksum = (uint)publicKey.GetChecksum();
        this.IsEncrypted = true;
        this.originalKeyValue = publicKey.KeyValue;

        var b = encrypted;
        this.encrypted0 = BitConverter.ToUInt64(b);
        b = b.Slice(sizeof(ulong));
        this.encrypted1 = BitConverter.ToUInt64(b);
        b = b.Slice(sizeof(ulong));
        this.encrypted2 = BitConverter.ToUInt64(b);
        b = b.Slice(sizeof(ulong));
        this.encrypted3 = BitConverter.ToUInt64(b);
    }

    private LinkageKey(PublicKey publicKey, PublicKey encryptionKey)
    {// Encrypt
        this.Checksum = (uint)publicKey.GetChecksum();
        this.Key = encryptionKey;
        this.IsEncrypted = true;
        this.originalKeyValue = publicKey.KeyValue;
    }

    #region FieldAndProperty

    [Key(0)]
    public readonly PublicKey Key;

    [Key(1)]
    public readonly uint Checksum; // (uint)FarmHash.Hash64(RawKey)

    [Key(2)]
    public readonly bool IsEncrypted;

    [Key(3)]
    private readonly byte originalKeyValue;

    [Key(4)]
    private readonly ulong encrypted0;

    [Key(5)]
    private readonly ulong encrypted1;

    [Key(6)]
    private readonly ulong encrypted2;

    [Key(7)]
    private readonly ulong encrypted3;

    #endregion

    public bool TryDecrypt(ECDiffieHellman ecdh, out PublicKey decrypted)
    {
        if (!this.IsEncrypted)
        {
            decrypted = this.Key;
            return true;
        }

        Span<byte> destination = stackalloc byte[32];

        using (var ecdh2 = this.Key.TryGetEcdh())
        {
            if (ecdh is null || ecdh2 is null)
            {
                decrypted = default;
                return false;
            }

            var material = ecdh.DeriveKeyMaterial(ecdh2.PublicKey);

            // Hash key material
            Sha3Helper.Get256_Span(material, material);

            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Key = material;

                Span<byte> source = stackalloc byte[32];
                Span<byte> iv = stackalloc byte[16];

                var c = source;
                BitConverter.TryWriteBytes(c, this.encrypted0);
                c = c.Slice(sizeof(ulong));
                BitConverter.TryWriteBytes(c, this.encrypted1);
                c = c.Slice(sizeof(ulong));
                BitConverter.TryWriteBytes(c, this.encrypted2);
                c = c.Slice(sizeof(ulong));
                BitConverter.TryWriteBytes(c, this.encrypted3);

                aes.TryDecryptCbc(source, iv, destination, out _, PaddingMode.None);
            }
        }

        decrypted = new(this.originalKeyValue, destination);
        if ((uint)decrypted.GetChecksum() != this.Checksum)
        {
            decrypted = default;
            return false;
        }

        return true;
    }

    public bool TryDecrypt(PrivateKey encryptionKey, out PublicKey decrypted)
    {
        var ecdh = encryptionKey.TryGetEcdh();
        if (ecdh == null)
        {
            decrypted = default;
            return false;
        }

        return this.TryDecrypt(ecdh, out decrypted);
    }

    public override string ToString()
    {
        Span<byte> span = stackalloc byte[EncodedLength];
        this.TryWriteBytes(span, out var written);
        span = span.Slice(0, written);
        return this.IsEncrypted ? $"[{Base64.Url.FromByteArrayToString(span)}]" : $"[!{Base64.Url.FromByteArrayToString(span)}]";
    }

    public string ToBase64()
    {
        Span<byte> span = stackalloc byte[EncodedLength];
        this.TryWriteBytes(span, out var written);
        span = span.Slice(0, written);
        return $"{Base64.Url.FromByteArrayToString(span)}";
    }

    internal bool TryWriteBytes(Span<byte> span, out int written)
    {
        if (span.Length < EncodedLength)
        {
            written = 0;
            return false;
        }

        var b = span;
        this.Key.TryWriteBytes(b, out _);
        b = b.Slice(PublicKey.EncodedLength);

        BitConverter.TryWriteBytes(b, this.Checksum);
        b = b.Slice(sizeof(uint));
        if (this.IsEncrypted)
        {
            b[0] = this.originalKeyValue;
            b = b.Slice(1);
            BitConverter.TryWriteBytes(b, this.encrypted0);
            b = b.Slice(sizeof(ulong));
            BitConverter.TryWriteBytes(b, this.encrypted1);
            b = b.Slice(sizeof(ulong));
            BitConverter.TryWriteBytes(b, this.encrypted2);
            b = b.Slice(sizeof(ulong));
            BitConverter.TryWriteBytes(b, this.encrypted3);

            written = LinkageKey.EncodedLength;
            return true;
        }
        else
        {
            written = PublicKey.EncodedLength + sizeof(uint);
            return true;
        }
    }
}