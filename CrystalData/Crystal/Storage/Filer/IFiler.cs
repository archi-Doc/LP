﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace CrystalData.Filer;

[TinyhandUnion(0, typeof(LocalFiler))]
internal partial interface IFiler
{
    string FilerPath { get; }

    Task<CrystalResult> PrepareAndCheck(StorageControl storage);

    Task Save(bool stop);

    void DeleteAll();

    CrystalResult Write(string path, ByteArrayPool.ReadOnlyMemoryOwner dataToBeShared);

    /// <summary>
    /// Delete the file matching the path.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns><see cref="CrystalResult"/>.</returns>
    CrystalResult Delete(string path);

    Task<CrystalMemoryOwnerResult> ReadAsync(string path, int sizeToRead, TimeSpan timeToWait);

    Task<CrystalResult> WriteAsync(string path, ByteArrayPool.ReadOnlyMemoryOwner dataToBeShared, TimeSpan timeToWait);

    Task<CrystalResult> DeleteAsync(string path, TimeSpan timeToWait);

    /*Task<CrystalMemoryOwnerResult> ReadAsync(string path, int sizeToRead)
        => this.ReadAsync(path, sizeToRead, TimeSpan.MinValue);

    Task<CrystalResult> WriteAsync(string path, ByteArrayPool.ReadOnlyMemoryOwner dataToBeShared)
        => this.WriteAsync(path, dataToBeShared, TimeSpan.MinValue);

    Task<CrystalResult> DeleteAsync(string path)
        => this.DeleteAsync(path, TimeSpan.MinValue);*/
}
