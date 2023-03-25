﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace CrystalData;

public interface ICrystalInternal
{
    DatumRegistry Datum { get; }

    CrystalOptions Options { get; }

    StorageControl Storage { get; }

    HimoGoshujinClass HimoGoshujin { get; }
}
