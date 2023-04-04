﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Crypto;
using Arc.Unit;
using CrystalData;
using CrystalData.Datum;
using LP;
using LP.Crystal;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace xUnitTest;

public static class TestHelper
{
    public static async Task<LpCrystal> CreateAndStartCrystal()
    {
        var options = new BigCrystalOptions() with
        {
            CrystalDirectory = $"Crystal[{RandomVault.Pseudo.NextUInt32():x4}]",
        };

        var builder = new CrystalControl.Builder();
        builder
            .Configure(context =>
            {
                context.AddSingleton<LpCrystal>();
                context.Services.Add(ServiceDescriptor.Transient(typeof(LpData), x => x.GetRequiredService<LpCrystal>().Object.Data));
            })
            .SetupOptions<BigCrystalOptions>((context, options) =>
            {
                options.CrystalDirectory = $"Crystal[{RandomVault.Pseudo.NextUInt32():x4}]";
            });

        var unit = builder.Build();
        var crystal = unit.Context.ServiceProvider.GetRequiredService<LpCrystal>();
        crystal.DatumRegistry.Register<FragmentDatum<Identifier>>(2, x => new FragmentDatumImpl<Identifier>(x));
        await crystal.PrepareAndLoad(new(FromScratch: true));
        return crystal;
    }

    public static async Task StopCrystal(IBigCrystal crystal, bool removeAll = true)
    {
        // await crystal.StopAsync(new(RemoveAll: removeAll)); // tempcode
        crystal.MemoryUsage.Is(0);
    }

    public static async Task StopAndStartCrystal(IBigCrystal crystal)
    {
        // await crystal.StopAsync(new()); // tempcode
        crystal.MemoryUsage.Is(0);
        // await crystal.StartAsync(new()); // tempcode
    }

    public static async Task<MergerCrystal> CreateAndStartMerger(int maxParent)
    {
        var builder = new CrystalControl.Builder();
        builder
            .Configure(context =>
            {
                context.AddSingleton<MergerCrystal>();
                context.Services.Add(ServiceDescriptor.Transient(typeof(LpData), x => x.GetRequiredService<MergerCrystal>().Object));
            })
            .SetupOptions<BigCrystalOptions>((context, options) =>
            {
                options.CrystalDirectory = $"Crystal[{RandomVault.Pseudo.NextUInt32():x4}]";
                options.MaxParentInMemory = maxParent;
            });

        var unit = builder.Build();
        var crystal = unit.Context.ServiceProvider.GetRequiredService<MergerCrystal>();
        await crystal.PrepareAndLoad(new(FromScratch: true));
        return crystal;
    }

    public static bool DataEquals(this CrystalMemoryResult dataResult, Span<byte> span)
    {
        return dataResult.Data.Span.SequenceEqual(span);
    }

    public static bool ByteArrayEquals(byte[]? array1, byte[]? array2, int length)
    {
        if (array1 == null || array2 == null)
        {
            return false;
        }
        else if (array1.Length < length || array2.Length < length)
        {
            return false;
        }

        for (var n = 0; n < length; n++)
        {
            if (array1[n] != array2[n])
            {
                return false;
            }
        }

        return true;
    }
}
