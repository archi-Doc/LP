// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using CrystalData;

public static class CrystalExtensions
{// -> implicit extension...
    public static bool IsSuccess(this CrystalResult result)
        => result == CrystalResult.Success;

    public static bool IsFailure(this CrystalResult result)
        => result != CrystalResult.Success;

    public static CrystalSourceAndResult FromSource(this CrystalResult result, CrystalSource crystalSource)
        => new(crystalSource, result);
}
