// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace CrystalData;

public readonly struct CrystalSourceAndResult
{
    public static readonly CrystalSourceAndResult Success = new(CrystalSource.NoSource, CrystalResult.Success);

    public CrystalSourceAndResult(CrystalSource source, CrystalResult result)
    {
        this.Source = source;
        this.Result = result;
    }

    public readonly CrystalSource Source;

    public readonly CrystalResult Result;

    public bool IsSuccess => this.Result == CrystalResult.Success;

    public bool IsFailure => this.Result != CrystalResult.Success;
}
