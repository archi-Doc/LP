﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1208 // System using directives should be placed before other using directives
#pragma warning disable SA1210 // Using directives should be ordered alphabetically by namespace
global using System;
global using System.Net;
global using System.Threading.Tasks;
global using Arc.Threading;
global using CrossChannel;
global using LP;
global using LP.Block;
global using LP.Options;
global using Tinyhand;
global using ValueLink;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using BigMachines;
using LP.Unit;
using SimpleCommandLine;
using ZenItz.Subcommands;

namespace ZenItz;

public class ZenControl
{
    public class Builder : UnitBuilder<Unit>
    {// Builder class for customizing dependencies.
        public Builder()
            : base()
        {
            this.Configure(context =>
            {
                // Main services
                context.AddSingleton<ZenControl>();
                context.AddSingleton<Zen>();
                context.AddSingleton<Itz>();

                Subcommands.ZenDirSubcommand.Configure(context);
            });
        }
    }

    public class Unit : BuiltUnit
    {// Unit class for customizing behaviors.
        public record Param();

        public Unit(UnitParameter parameter)
            : base(parameter)
        {
        }
    }

    public ZenControl(IServiceProvider serviceProvider, Zen zen, Itz itz)
    {
        // this.ServiceProvider = serviceProvider;
        this.Zen = zen;
        this.Itz = itz;
    }

    // public IServiceProvider ServiceProvider { get; }

    public Zen Zen { get; }

    public Itz Itz { get; }

    public bool ExaltationOfIntegrality { get; } = true; // by Baxter.
}
