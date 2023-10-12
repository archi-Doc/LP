﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace CrystalData;

public interface IStorageObsolete
{
    long StorageUsage { get; }

    void SetTimeout(TimeSpan timeout);

    Task<CrystalResult> PrepareAndCheck(PrepareParam param, StorageConfiguration storageConfiguration, bool createNew);

    Task SaveStorage();

    Task<CrystalMemoryOwnerResult> GetAsync(ref ulong fileId);

    CrystalResult PutAndForget(ref ulong fileId, ByteArrayPool.ReadOnlyMemoryOwner memoryToBeShared);

    Task<CrystalResult> PutAsync(ref ulong fileId, ByteArrayPool.ReadOnlyMemoryOwner memoryToBeShared);

    CrystalResult DeleteAndForget(ref ulong fileId);

    Task<CrystalResult> DeleteAsync(ref ulong fileId);

    Task<CrystalResult> DeleteStorageAsync();
}
