﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Arc.Crypto;
using LP;
using SimpleCommandLine;
using Tinyhand;

namespace LP.Subcommands;

[SimpleCommand("dump")]
public class DumpSubcommand : ISimpleCommand<DumpOptions>
{
    public DumpSubcommand(Control control)
    {
        this.Control = control;
    }

    public void Run(DumpOptions options, string[] args)
    {
        var logger = Logger.Priority;
        logger.Information($"Dump:");
        logger.Information(System.Environment.OSVersion.ToString());
        this.Control.NetControl.Terminal.Dump(logger);
    }

    public Control Control { get; set; }
}

public record DumpOptions
{
    [SimpleOption("count", description: "Count")]
    public int Count { get; init; }

    public override string ToString() => $"{this.Count}";
}
