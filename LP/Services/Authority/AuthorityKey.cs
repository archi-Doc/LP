﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using LP.Services;

namespace LP;

internal sealed class AuthorityKey
{
    public AuthorityKey(Authority authority, string name, byte[] encrypted)
    {
        this.authority = authority;
        this.Name = name;
        this.encrypted = encrypted;
    }

    public async Task<(AuthorityResult Result, byte[] Signature)> SignData(Credit credit, byte[] data)
    {
        var result = await this.Prepare().ConfigureAwait(false);
        if (result != AuthorityResult.Success)
        {
            return (result, Array.Empty<byte>());
        }

        var signature = this.authorityInfo!.SignData(credit, data);
        if (signature == null)
        {
            signature = Array.Empty<byte>();
            result = AuthorityResult.InvalidData;
        }

        return (result, signature);
    }

    public async Task<AuthorityResult> VerifyData(Credit credit, byte[] data, byte[] signature)
    {
        var result = await this.Prepare().ConfigureAwait(false);
        if (result != AuthorityResult.Success)
        {
            return result;
        }

        if (this.authorityInfo!.VerifyData(credit, data, signature))
        {
            return AuthorityResult.Success;
        }
        else
        {
            return AuthorityResult.InvalidSignature;
        }
    }

    public async Task<(AuthorityResult Result, AuthorityInfo? AuthorityInfo)> GetInfo()
    {
        var result = await this.Prepare().ConfigureAwait(false);
        return (result, this.authorityInfo);
    }

    public string Name { get; private set; }

    public long ExpirationMics { get; private set; }

    private async Task<AuthorityResult> Prepare()
    {
        if (this.authorityInfo != null)
        {
            if (this.authorityInfo.Lifetime == AuthorityLifetime.PeriodOfTime)
            {// Periof of time
                if (Mics.GetUtcNow() > this.ExpirationMics)
                {// Expired
                    this.authorityInfo = null;
                }
            }

            if (this.authorityInfo != null)
            {
                return AuthorityResult.Success;
            }
        }

        // Try to get AuthorityInfo.
        if (!PasswordEncrypt.TryDecrypt(this.encrypted, string.Empty, out var decrypted))
        {
            while (true)
            {
                var passPhrase = await this.authority.UserInterfaceService.RequestPassword(Hashed.Authority.EnterPassword, this.Name).ConfigureAwait(false);
                if (passPhrase == null)
                {
                    return AuthorityResult.Canceled;
                }

                if (PasswordEncrypt.TryDecrypt(this.encrypted, passPhrase, out decrypted))
                {
                    break;
                }
            }
        }

        // Deserialize
        try
        {
            this.authorityInfo = TinyhandSerializer.Deserialize<AuthorityInfo>(decrypted);
        }
        catch
        {
        }

        if (this.authorityInfo != null)
        {
            if (this.authorityInfo.Lifetime == AuthorityLifetime.PeriodOfTime)
            {
                this.ExpirationMics = Mics.GetUtcNow() + this.authorityInfo.LifeMics;
            }

            return AuthorityResult.Success;
        }
        else
        {
            return AuthorityResult.InvalidData;
        }
    }

    private Authority authority;
    private byte[] encrypted;
    private AuthorityInfo? authorityInfo;
}
