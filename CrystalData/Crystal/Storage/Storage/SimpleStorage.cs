﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using CrystalData.Filer;

#pragma warning disable SA1124 // Do not use regions
#pragma warning disable SA1204

namespace CrystalData.Storage;

[TinyhandObject]
internal partial class SimpleStorage : IStorage
{
    private const string SimpleStorageMain = "Simple.main";
    private const string SimpleStorageBack = "Simple.back";

    public SimpleStorage()
    {
    }

    public override string ToString()
        => $"SimpleStorage {StorageHelper.ByteToString(this.StorageUsage)}/{StorageHelper.ByteToString(this.StorageCapacity)}";

    #region FieldAndProperty

    [Key(0)]
    public long StorageCapacity { get; set; }

    [Key(1)]
    public long StorageUsage { get; private set; } // lock (this.syncObject)

    private StorageControl? storage;
    private IFiler? filer;
    private object syncObject = new();
    private Dictionary<uint, int> dictionary = new();

    #endregion

    #region IStorage

    StorageResult IStorage.Put(ref ulong fileId, ByteArrayPool.ReadOnlyMemoryOwner dataToBeShared)
    {
        if (this.filer == null)
        {
            return StorageResult.NoFiler;
        }

        this.PutInternal(ref fileId, dataToBeShared.Memory.Length);
        return this.filer.Write(FileToPath(FileIdToFile(fileId)), dataToBeShared);
    }

    StorageResult IStorage.Delete(ref ulong fileId)
    {
        if (this.filer == null)
        {
            return StorageResult.NoFiler;
        }

        var file = FileIdToFile(fileId);
        if (file == 0)
        {
            return StorageResult.NoFile;
        }

        lock (this.syncObject)
        {
            if (!this.dictionary.Remove(file))
            {// Not found
                fileId = 0;
                return StorageResult.NoFile;
            }

            // this.dictionary.Add(file, -1);
        }

        fileId = 0;
        return this.filer.Delete(FileToPath(file));
    }

    Task<StorageMemoryOwnerResult> IStorage.GetAsync(ref ulong fileId, TimeSpan timeToWait)
    {
        if (this.filer == null)
        {
            return Task.FromResult(new StorageMemoryOwnerResult(StorageResult.NoFiler));
        }

        var file = FileIdToFile(fileId);
        int size;
        lock (this.syncObject)
        {
            if (!this.dictionary.TryGetValue(file, out size))
            {// Not found
                fileId = 0;
                return Task.FromResult(new StorageMemoryOwnerResult(StorageResult.NoFile));
            }
        }

        return this.filer.ReadAsync(FileToPath(file), size, timeToWait);
    }

    Task<StorageResult> IStorage.PutAsync(ref ulong fileId, ByteArrayPool.ReadOnlyMemoryOwner dataToBeShared, TimeSpan timeToWait)
    {
        if (this.filer == null)
        {
            return Task.FromResult(StorageResult.NoFiler);
        }

        this.PutInternal(ref fileId, dataToBeShared.Memory.Length);
        return this.filer.WriteAsync(FileToPath(FileIdToFile(fileId)), dataToBeShared, timeToWait);
    }

    Task<StorageResult> IStorage.DeleteAsync(ref ulong fileId, TimeSpan timeToWait)
    {
        if (this.filer == null)
        {
            return Task.FromResult(StorageResult.NoFiler);
        }

        var file = FileIdToFile(fileId);
        if (file == 0)
        {
            return Task.FromResult(StorageResult.NoFile);
        }

        lock (this.syncObject)
        {
            this.dictionary.Remove(file);
        }

        fileId = 0;
        return this.filer.DeleteAsync(FileToPath(file), timeToWait);
    }

    #endregion

    async Task<StorageResult> IStorage.PrepareAndCheck(StorageControl storage, IFiler filer)
    {
        this.storage = storage;
        this.filer = filer;

        var result = await this.TryLoad(SimpleStorageMain).ConfigureAwait(false);
        if (result != StorageResult.Success)
        {
            result = await this.TryLoad(SimpleStorageBack).ConfigureAwait(false);
        }

        return result;
    }

    async Task IStorage.Save()
    {
        byte[] byteArray;
        lock (this.syncObject)
        {
            byteArray = TinyhandSerializer.Serialize(this.dictionary);
        }

        if (this.filer != null)
        {
            await this.filer.WriteAsync(SimpleStorageMain, new(byteArray), TimeSpan.MinValue);
            await this.filer.WriteAsync(SimpleStorageBack, new(byteArray), TimeSpan.MinValue);
        }
    }

    private async Task<StorageResult> TryLoad(string path)
    {
        if (this.filer == null)
        {
            return StorageResult.NoFiler;
        }

        var result = await this.filer.ReadAsync(path, -1, TimeSpan.MinValue);
        if (!result.IsSuccess)
        {
            return result.Result;
        }

        if (!HashHelper.CheckFarmHashAndGetData(result.Data.Memory, out var data))
        {
            return StorageResult.CorruptedData;
        }

        try
        {
            if (TinyhandSerializer.Deserialize<Dictionary<uint, int>>(data) is { } dictionary)
            {
                this.dictionary = dictionary;
            }
        }
        catch
        {
            return StorageResult.CorruptedData;
        }

        return StorageResult.Success;
    }

    #region Helper

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FileIdToFile(ulong fileId) => (uint)(fileId >> 32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong FileToFileId(uint file) => file << 32;

    private static string FileToPath(uint file)
    {
        Span<char> c = stackalloc char[9];
        var n = 0;

        c[n++] = UInt32ToChar(file >> 28);
        c[n++] = UInt32ToChar(file >> 24);

        c[n++] = '/';

        c[n++] = UInt32ToChar(file >> 20);
        c[n++] = UInt32ToChar(file >> 16);
        c[n++] = UInt32ToChar(file >> 12);
        c[n++] = UInt32ToChar(file >> 8);
        c[n++] = UInt32ToChar(file >> 4);
        c[n++] = UInt32ToChar(file);

        return c.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char UInt32ToChar(uint x)
    {
        var a = x & 0xF;
        if (a < 10)
        {
            return (char)('0' + a);
        }
        else
        {
            return (char)('W' + a);
        }
    }

    #endregion

    #region Internal

    private void PutInternal(ref ulong fileId, int dataSize)
    {
        var file = FileIdToFile(fileId);
        lock (this.syncObject)
        {
            if (file != 0 && this.dictionary.TryGetValue(file, out var size))
            {
                // if (dataSize > size)
                {
                    this.StorageUsage += dataSize - size;
                }
            }
            else
            {// Not found
                file = this.NewFile();
                this.StorageUsage += dataSize;
            }
        }

        fileId = FileToFileId(file);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint NewFile()
    {// lock (this.syncObject)
        while (true)
        {
            var file = RandomVault.Pseudo.NextUInt32();
            if (!this.dictionary.TryAdd(file, 0))
            {
                return file;
            }
        }
    }

    #endregion
}
