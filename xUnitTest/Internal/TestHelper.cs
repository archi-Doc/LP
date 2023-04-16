// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

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
    public static async Task<IBigCrystal<LpData>> CreateAndStartCrystal()
    {
        var builder = new CrystalControl.Builder();
        builder
            .ConfigureCrystal(context =>
            {
                var directory = $"Crystal[{RandomVault.Pseudo.NextUInt32():x4}]";
                context.AddBigCrystal<LpData>(
                    new(
                        datumRegistry =>
                        {
                            datumRegistry.Register<BlockDatum>(1, x => new BlockDatumImpl(x));
                            datumRegistry.Register<FragmentDatum<Identifier>>(2, x => new FragmentDatumImpl<Identifier>(x));
                        },
                        SaveMethod.None,
                        new LocalDirectoryConfiguration(directory),
                        new SimpleStorageConfiguration(new LocalDirectoryConfiguration(directory))));
            });

        var unit = builder.Build();
        var crystalizer = unit.Context.ServiceProvider.GetRequiredService<Crystalizer>();

        var crystal = crystalizer.GetBigCrystal<LpData>();
        await crystal.PrepareAndLoad(new(FromScratch: true));
        return crystal;
    }

    public static async Task UnloadAndDeleteAll(IBigCrystal crystal)
    {
        await crystal.Crystalizer.SaveAll(true);
        crystal.MemoryUsage.Is(0);
        await crystal.Crystalizer.DeleteAll();
    }

    public static async Task StopAndStartCrystal(IBigCrystal crystal)
    {
        await crystal.Crystalizer.SaveAll(true);
        // await crystal.Save(true);
        crystal.MemoryUsage.Is(0);
        // await crystal.StartAsync(new()); // tempcode
    }

    public static async Task<IBigCrystal<MergerData>> CreateAndStartMerger(int maxParent)
    {
        var builder = new CrystalControl.Builder();
        builder
            .ConfigureCrystal(context =>
            {
                var directory = $"Crystal[{RandomVault.Pseudo.NextUInt32():x4}]";
                context.AddBigCrystal<MergerData>(new BigCrystalConfiguration() with
                {
                    RegisterDatum = registry =>
                    {
                        registry.Register<BlockDatum>(1, x => new BlockDatumImpl(x));
                    },
                    DirectoryConfiguration = new LocalDirectoryConfiguration(directory),
                    StorageConfiguration = new SimpleStorageConfiguration(new LocalDirectoryConfiguration(directory)),
                    MaxParentInMemory = maxParent,
                });
            });

        var unit = builder.Build();
        var crystalizer = unit.Context.ServiceProvider.GetRequiredService<Crystalizer>();

        var crystal = crystalizer.GetBigCrystal<MergerData>();
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
