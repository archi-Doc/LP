﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using SimpleCommandLine;
using Tinyhand.IO;

namespace LP.Options;

[TinyhandObject(ImplicitKeyAsName = true)]
public partial record LPOptions
{
    [SimpleOption("load", description: "Options path")]
    public string OptionsPath { get; init; } = string.Empty;

    [SimpleOption("development", description: "Development")]
    public bool Development { get; init; } = false;

    [SimpleOption("mode", description: "LP mode (relay, merger, user)")]
    public string Mode { get; init; } = string.Empty;

    [SimpleOption("directory", description: "Root directory")]
    public string Directory { get; init; } = string.Empty;

    [SimpleOption("keyvault", description: "Key Vault path")]
    public string KeyVault { get; init; } = "KeyVault.tinyhand";

    [SimpleOption("name", description: "Node name")]
    public string NodeName { get; init; } = string.Empty;

    [SimpleOption("ns", description: "Netsphere option")]
    public NetsphereOptions NetsphereOptions { get; init; } = default!;

    [SimpleOption("zen", description: "ZenItz option")]
    public ZenItzOptions ZenItzOptions { get; init; } = default!;

    public override string ToString()
    {
        return $"{this.NetsphereOptions.ToString()}";
    }

    /*public bool TryLoad()
    {
        if (!string.IsNullOrEmpty(this.Options))
        {
            try
            {
                var utf8 = File.ReadAllBytes(this.Options);
                var writer = default(TinyhandWriter);
                TinyhandTreeConverter.FromUtf8ToBinary(utf8, ref writer);
                var reader = new TinyhandReader(writer.FlushAndGetReadOnlySequence());
                this.Deserialize(ref reader, TinyhandSerializerOptions.Standard);
                return true;
            }
            catch
            {
            }
        }

        return false;
    }*/
}
