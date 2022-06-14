﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Crypto;
using DryIoc;
using LP;
using SimpleCommandLine;
using Tinyhand;
using ZenItz;

namespace LP.Subcommands;

[SimpleCommand("zendir", IsSubcommand = true, Description = "Zen directory subcommand")]
public class ZenDirSubcommand : ISimpleCommandAsync
{
    public static void Register(Container container)
    {
        commandTypes = new Type[]
        {
            typeof(ZenDirSubcommandLs),
            typeof(ZenDirSubcommandAdd),
        };

        foreach (var x in commandTypes)
        {
            container.Register(x, Reuse.Singleton);
        }
    }

    public ZenDirSubcommand(ZenControl control)
    {
        this.Control = control;
    }

    public async Task Run(string[] args)
    {
        if (commandTypes == null)
        {
            return;
        }
        else if (subcommandParser == null)
        {
            subcommandParser ??= new(commandTypes, ZenControl.SubcommandParserOptions);
        }

        await subcommandParser.ParseAndRunAsync(args);
    }

    private static SimpleParser? subcommandParser;
    private static Type[]? commandTypes;

    public ZenControl Control { get; set; }
}
