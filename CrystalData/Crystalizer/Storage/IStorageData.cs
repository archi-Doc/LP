﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace CrystalData;

public interface IStorageData
{
    ValueTask<bool> Unload();
}
