﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Security.Cryptography;
using System.Text;

namespace LP.T3CS;

[TinyhandObject]
public sealed partial class EncryptionPrivateKey : PrivateKey, IEquatable<EncryptionPrivateKey>
{
    #region Unique

    private ECDiffieHellman? ecdh;

    public static EncryptionPrivateKey CreateEncryptionKey()
    {
        var p = KeyHelper.CreateEcdhParameters();
        return new EncryptionPrivateKey(p.Q.X!, p.Q.Y!, p.D!);
    }

    public static EncryptionPrivateKey CreateEncryptionKey(ReadOnlySpan<byte> seed)
    {
        var p = KeyHelper.CreateEcdhParameters(seed);
        return new EncryptionPrivateKey(p.Q.X!, p.Q.Y!, p.D!);
    }

    public ECDiffieHellman? TryGetEcdh()
    {
        return this.ecdh ??= KeyHelper.CreateEcdhFromD(this.d);
    }

    #endregion

    #region TypeSpecific

    public EncryptionPrivateKey()
    {
    }

    private EncryptionPrivateKey(byte[] x, byte[] y, byte[] d)
        : base(KeyClass.T3CS_Encryption, x, y, d)
    {
    }

    public EncryptionPublicKey ToPublicKey()
        => new(this.keyValue, this.x.AsSpan());

    public override bool Validate()
        => this.KeyClass == KeyClass.T3CS_Encryption && base.Validate();

    public bool IsSameKey(EncryptionPublicKey publicKey)
        => publicKey.IsSameKey(this);

    public bool Equals(EncryptionPrivateKey? other)
    {
        if (other == null)
        {
            return false;
        }

        return this.keyValue == other.keyValue &&
            this.x.AsSpan().SequenceEqual(other.x);
    }

    #endregion
}
