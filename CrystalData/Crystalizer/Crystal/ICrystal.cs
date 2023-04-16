// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace CrystalData;

public interface ICrystal
{
    Crystalizer Crystalizer { get; }

    CrystalConfiguration CrystalConfiguration { get; }

    object Object { get; }

    bool Prepared { get; }

    IFiler Filer { get; }

    IStorage Storage { get; }

    void Configure(CrystalConfiguration configuration);

    void ConfigureFile(FileConfiguration configuration);

    void ConfigureStorage(StorageConfiguration configuration);

    Task<CrystalSourceAndResult> PrepareAndLoad(CrystalPrepareParam? param = null);

    Task<CrystalResult> Save(bool unload = false);

    Task<CrystalResult> Delete();

    void Terminate();
}

public interface ICrystal<TData> : ICrystal
    where TData : IJournalObject, ITinyhandSerialize<TData>, ITinyhandReconstruct<TData>
{
    public new TData Object { get; }
}
