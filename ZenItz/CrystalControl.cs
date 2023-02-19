﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1210 // Using directives should be ordered alphabetically by namespace

global using System;
global using System.Threading.Tasks;
global using Arc.Threading;
global using Arc.Unit;
global using LP;
global using LP.Block;
global using Tinyhand;
global using ValueLink;
using Microsoft.Extensions.DependencyInjection;

namespace CrystalData;

public class CrystalControl
{
    public class Builder : UnitBuilder<Unit>
    {// Builder class for customizing dependencies.
        public Builder()
            : base()
        {
            this.Configure(context =>
            {
                LPBase.Configure(context);

                // Main services
                context.AddSingleton<CrystalControl>();
                context.AddSingleton<LpCrystal>();
                context.Services.Add(ServiceDescriptor.Transient(typeof(LpData), x => x.GetRequiredService<LpCrystal>().Data));
                context.AddSingleton<Itz>();

                // Subcommands
                Subcommands.CrystalDirSubcommand.Configure(context);
                Subcommands.CrystalTempSubcommand.Configure(context);
            });
        }
    }

    public class Unit : BuiltUnit
    {// Unit class for customizing behaviors.
        public record Param();

        public Unit(UnitContext context)
            : base(context)
        {
        }
    }

    public CrystalControl(UnitContext unitContext, LpCrystal crystal, CrystalOptions options, Itz itz)
    {
        this.unitContext = unitContext;
        this.Crystal = crystal;
        this.Crystal.Options = options;
        this.Itz = itz;
    }

    public Crystal<TData> CreateCrystal<TData>(CrystalOptions options)
        where TData : BaseData
    {
        return new Crystal<TData>(
            this.unitContext.ServiceProvider.GetRequiredService<UnitCore>(),
            options,
            this.unitContext.ServiceProvider.GetRequiredService<ILogger<Crystal<TData>>>());
    }

    public LpCrystal Crystal { get; }

    public Itz Itz { get; }

    public bool ExaltationOfIntegrality { get; } = true; // by Baxter.

    private readonly UnitContext unitContext;
}
