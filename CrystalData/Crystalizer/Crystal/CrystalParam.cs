// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace CrystalData;

public record CrystalPrepareParam(bool ForceStart = false, CrystalPrepareQueryDelegate? QueryDelegate = null, bool FromScratch = false, bool AllowExistingOnly = false)
{
    public static readonly CrystalPrepareParam Default = new(true);

    public Task<AbortOrContinue> Query(CrystalSourceAndResult query, string[]? list = null)
        => this.QueryDelegate == null || this.ForceStart ? Task.FromResult(AbortOrContinue.Continue) : this.QueryDelegate(query, list);
}

public record CrystalStopParam(bool RemoveAll = false)
{
    public static readonly CrystalStopParam Default = new(false);
}

public enum CrystalStartResult
{
    Success,
    FileNotFound,
    FileError,
    DirectoryNotFound,
    DirectoryError,
    NoDirectoryAvailable,
    DeserializeError,
    NoJournal,
}

public delegate Task<AbortOrContinue> CrystalPrepareQueryDelegate(CrystalSourceAndResult query, string[]? list);
