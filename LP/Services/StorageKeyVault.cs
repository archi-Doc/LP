﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace LP;

internal class StorageKeyVault : IStorageKey
{
    private const string Prefix = "S3Bucket/";
    private const char Separator = '=';

    public StorageKeyVault(Vault vault)
    {
        this.vault = vault;
    }

    bool IStorageKey.TryGetKey(string bucket, out AccessKeyPair accessKeyPair)
    {
        accessKeyPair = default;
        if (!this.vault.TryGet(Prefix + bucket, out var decrypted))
        {
            return false;
        }

        try
        {
            var st = this.utf8.GetString(decrypted);
            var array = st.Split(Separator);
            if (array.Length >= 2)
            {
                accessKeyPair = new(array[0], array[1]);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private Vault vault;
    private Encoding utf8 = new UTF8Encoding(true, false);
}