﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1208 // System using directives should be placed before other using directives
#pragma warning disable SA1210 // Using directives should be ordered alphabetically by namespace

global using System.Net;
global using Arc.Crypto;
global using Arc.Threading;
global using Arc.Unit;
global using BigMachines;
global using Tinyhand;
global using ValueLink;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Netsphere.Logging;
using Netsphere.Machines;
using Netsphere.Misc;
using Netsphere.Server;
using Netsphere.Stats;

namespace Netsphere;

public class NetControl : UnitBase, IUnitPreparable
{
    public const int MaxPacketLength = 1432; // 1500 - 60 - 8 = 1432 bytes
    public const int MaxDataSize = 4 * 1024 * 1024; // 4 MB
    public const int MinPort = 49152; // Ephemeral port 49152 - 60999
    public const int MaxPort = 60999;

    public class Builder : UnitBuilder<Unit>
    {
        public Builder()
            : base()
        {
            this.Configure(context =>
            {
                // Main services
                context.AddSingleton<NetControl>();
                context.AddSingleton<NetBase>();
                context.AddSingleton<EssentialAddress>();
                context.AddSingleton<NetStats>();
                context.AddSingleton<NtpCorrection>();
                context.AddSingleton<NetResponder>();
                context.AddSingleton<NetTerminal>();
                // context.Services.Add(new ServiceDescriptor(typeof(NetService), x => new NetService(x), ServiceLifetime.Transient));
                // context.AddTransient<NetService>(); // serviceCollection.RegisterDelegate(x => new NetService(container), Reuse.Transient);

                // Stream logger
                context.Services.Add(ServiceDescriptor.Singleton(typeof(IdFileLogger<>), typeof(IdFileLoggerFactory<>)));
                context.TryAddSingleton<IdFileLoggerOptions>();

                // Machines
                // context.AddTransient<EssentialNetMachine>();
                context.AddTransient<NtpMachine>();
                context.AddTransient<NetStatsMachine>();

                // Subcommands
                context.AddSubcommand(typeof(LP.Subcommands.NetTestSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.NetCleanSubcommand));
            });
        }
    }

    public class Unit : BuiltUnit
    {
        public record Param(bool EnableServer, string NodeName, NetsphereOptions Options, bool AllowUnsafeConnection, Func<ServerConnection, ConnectionContext>? newConnectionContext = null);

        public static Param DefaultParam { get; } = new(true, "Test", new(), false);

        public Unit(UnitContext context)
            : base(context)
        {
        }

        public async Task RunStandalone(Param param)
        {
            var netBase = this.Context.ServiceProvider.GetRequiredService<NetBase>();
            netBase.SetParameter(param.EnableServer, param.Options);
            netBase.NodeName = param.NodeName;
            netBase.AllowUnsafeConnection = param.AllowUnsafeConnection;
            if (param.newConnectionContext is not null)
            {
                netBase.NewConnectionContext = param.newConnectionContext;
            }

            var netControl = this.Context.ServiceProvider.GetRequiredService<NetControl>();
            if (param.EnableServer)
            {
                // netControl.SetupServer(param.NewServerContext, param.NewCallContext);
            }

            this.Context.SendPrepare(new());
            await this.Context.SendRunAsync(new(ThreadCore.Root)).ConfigureAwait(false);
        }
    }

    public NetControl(UnitContext context, UnitLogger unitLogger, NetBase netBase, NetStats netStats, NetResponder netResponder, NetTerminal netTerminal)
        : base(context)
    {
        this.unitLogger = unitLogger;
        this.ServiceProvider = context.ServiceProvider;
        this.NetBase = netBase;
        this.NetStats = netStats;
        this.NetResponder = netResponder;

        this.NetTerminal = netTerminal;
        this.NetTerminal.Initialize(this.NetResponder, false);
        if (this.NetBase.NetsphereOptions.EnableAlternative)
        {// For debugging
            this.Alternative = new(context, unitLogger, netBase, netStats);
            this.Alternative.Initialize(this.NetResponder, true);
        }
    }

    #region FieldAndProperty

    public NetBase NetBase { get; }

    public NetStats NetStats { get; }

    public NetResponder NetResponder { get; }

    public NetTerminal NetTerminal { get; }

    public NetTerminal? Alternative { get; }

    internal IServiceProvider ServiceProvider { get; }

    private UnitLogger unitLogger;

    #endregion

    public void Prepare(UnitMessage.Prepare message)
    {
    }

    private void Dump(ILog logger)
    {
        logger.Log($"Dump:");
    }
}
