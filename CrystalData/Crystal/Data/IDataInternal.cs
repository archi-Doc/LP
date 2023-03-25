﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using CrystalData.Datum;

namespace CrystalData;

public interface IDataInternal
{
    ICrystalInternal CrystalInternal { get; }

    DatumRegistry Data { get; }

    CrystalOptions Options { get; }

    void DatumToStorage<TDatum>(ByteArrayPool.ReadOnlyMemoryOwner memoryToBeShared)
        where TDatum : IDatum;

    Task<CrystalMemoryOwnerResult> StorageToDatum<TDatum>()
        where TDatum : IDatum;

    void DeleteStorage<TDatum>()
        where TDatum : IDatum;

    void SaveDatum(ushort id, bool unload);
}
