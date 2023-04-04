﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace CrystalData;

public partial interface IStorage
{
    long StorageUsage { get; }

    Task<CrystalResult> PrepareAndCheck(Crystalizer crystalizer, StorageConfiguration storageConfiguration, bool createNew);

    Task Save();

    CrystalResult Put(ref ulong fileId, ByteArrayPool.ReadOnlyMemoryOwner memoryToBeShared);

    CrystalResult Delete(ref ulong fileId);

    Task<CrystalMemoryOwnerResult> GetAsync(ref ulong fileId, TimeSpan timeToWait);

    Task<CrystalResult> PutAsync(ref ulong fileId, ByteArrayPool.ReadOnlyMemoryOwner memoryToBeShared, TimeSpan timeToWait);

    Task<CrystalResult> DeleteAsync(ref ulong fileId, TimeSpan timeToWait);

    Task<CrystalMemoryOwnerResult> GetAsync(ref ulong fileId)
        => this.GetAsync(ref fileId, TimeSpan.MinValue);

    Task<CrystalResult> PutAsync(ref ulong fileId, ByteArrayPool.ReadOnlyMemoryOwner memoryToBeShared)
        => this.PutAsync(ref fileId, memoryToBeShared, TimeSpan.MinValue);

    Task<CrystalResult> DeleteAsync(ref ulong fileId)
        => this.DeleteAsync(ref fileId, TimeSpan.MinValue);
}

/*public interface IStorage<TData> : IStorage
    where TData : ITinyhandSerialize<TData>, ITinyhandReconstruct<TData>
{
}*/