﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace CrystalData;

public enum Crystalization
{
    None,
    Manual,
    Periodic,
    Instant,
}

public record CrystalConfiguration
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

    public static readonly CrystalConfiguration Default = new();

    public CrystalConfiguration()
    {
        this.Crystalization = Crystalization.None;
        this.FilerConfiguration = EmptyFilerConfiguration.Default;
    }

    public CrystalConfiguration(Crystalization crystalization, FilerConfiguration filerConfiguration, StorageConfiguration? storageConfiguration = null)
    {
        this.Crystalization = crystalization;
        this.Interval = DefaultInterval;
        this.FilerConfiguration = filerConfiguration;
        this.StorageConfiguration = storageConfiguration ?? EmptyStorageConfiguration.Default;
    }

    public CrystalConfiguration(TimeSpan interval, FilerConfiguration filerConfiguration, StorageConfiguration? storageConfiguration = null)
    {
        this.Crystalization = Crystalization.Periodic;
        this.Interval = interval;
        this.FilerConfiguration = filerConfiguration;
        this.StorageConfiguration = storageConfiguration ?? EmptyStorageConfiguration.Default;
    }

    public Crystalization Crystalization { get; init; }

    public TimeSpan Interval { get; init; }

    public FilerConfiguration FilerConfiguration { get; init; }

    public StorageConfiguration StorageConfiguration { get; init; }
}
