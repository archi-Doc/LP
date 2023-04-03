﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace CrystalData.Filer;

[TinyhandUnion(0, typeof(EmptyFiler))]
[TinyhandUnion(1, typeof(LocalFiler))]
[TinyhandUnion(2, typeof(S3Filer))]
public partial interface IRawFiler
{
    // string FilerPath { get; }

    Task<CrystalResult> PrepareAndCheck(Crystalizer crystalizer, FilerConfiguration configuration);

    Task Terminate();

    CrystalResult Write(string path, long offset, ByteArrayPool.ReadOnlyMemoryOwner dataToBeShared);

    /// <summary>
    /// Delete the file matching the path.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns><see cref="CrystalResult"/>.</returns>
    CrystalResult Delete(string path);

    Task<CrystalMemoryOwnerResult> ReadAsync(string path, long offset, int length, TimeSpan timeToWait);

    Task<CrystalMemoryOwnerResult> ReadAsync(string path, long offset, int length)
        => this.ReadAsync(path, offset, length, TimeSpan.MinValue);

    Task<CrystalResult> WriteAsync(string path, long offset, ByteArrayPool.ReadOnlyMemoryOwner dataToBeShared, TimeSpan timeToWait);

    Task<CrystalResult> WriteAsync(string path, long offset, ByteArrayPool.ReadOnlyMemoryOwner dataToBeShared)
        => this.WriteAsync(path, offset, dataToBeShared, TimeSpan.MinValue);

    Task<CrystalResult> DeleteAsync(string path, TimeSpan timeToWait);

    Task<CrystalResult> DeleteAsync(string path)
        => this.DeleteAsync(path);
}
