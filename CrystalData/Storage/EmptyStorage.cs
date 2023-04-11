﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Linq.Expressions;

namespace CrystalData.Storage;

public partial class EmptyStorage : IStorage
{
    public static readonly EmptyStorage Default = new();

    long IStorage.StorageUsage => 0;

    void IStorage.SetTimeout(TimeSpan timeout)
        => Expression.Empty();

    CrystalResult IStorage.Delete(ref ulong fileId)
        => CrystalResult.Success;

    Task<CrystalResult> IStorage.DeleteAsync(ref ulong fileId)
        => Task.FromResult(CrystalResult.Success);

    Task<CrystalMemoryOwnerResult> IStorage.GetAsync(ref ulong fileId)
        => Task.FromResult(new CrystalMemoryOwnerResult(CrystalResult.Success));

    Task<CrystalResult> IStorage.PrepareAndCheck(Crystalizer crystalizer, StorageConfiguration storageConfiguration, bool createNew)
        => Task.FromResult(CrystalResult.Success);

    CrystalResult IStorage.Put(ref ulong fileId, ByteArrayPool.ReadOnlyMemoryOwner memoryToBeShared)
        => CrystalResult.Success;

    Task<CrystalResult> IStorage.PutAsync(ref ulong fileId, ByteArrayPool.ReadOnlyMemoryOwner memoryToBeShared)
        => Task.FromResult(CrystalResult.Success);

    Task IStorage.Save()
        => Task.CompletedTask;
}
