﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using Arc.Crypto;
using LP;
using SimpleCommandLine;
using Tinyhand;

namespace LP.Subcommands;

[SimpleCommand("seedphrase")]
public class SeedphraseSubcommand : ISimpleCommandAsync
{
    public SeedphraseSubcommand(ILogger<SeedphraseSubcommand> logger, Control control, Seedphrase seedPhrase)
    {
        this.logger = logger;
        this.Control = control;
        this.seedPhrase = seedPhrase;
    }

    public async Task RunAsync(string[] args)
    {
        this.logger.TryGet()?.Log($"Seedphrase subcommand: ");

        var phrase = this.seedPhrase.Create();
        this.logger.TryGet()?.Log($"{phrase}");

        var seed = this.seedPhrase.TryGetSeed(phrase);
        this.logger.TryGet()?.Log($"{seed}");
    }

    public Control Control { get; set; }

    private ILogger<SeedphraseSubcommand> logger;
    private Seedphrase seedPhrase;
}
