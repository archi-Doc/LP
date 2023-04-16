// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using CrystalData.Filer;
using CrystalData.Results;
using CrystalData.Storage;

namespace CrystalData;

public sealed class StorageGroup
{
    public const long DefaultStorageCapacity = 1024L * 1024 * 1024 * 10; // 10GB
    public const int StorageRotationThreshold = (int)StorageHelper.Megabytes * 100; // 100 MB

    internal StorageGroup(Crystalizer crystalizer)
    {
        this.Crystalizer = crystalizer;
        this.StorageKey = crystalizer.StorageKey;
    }

    #region PropertyAndField

    public Crystalizer Crystalizer { get; }

    public IStorageKey StorageKey { get; }

    private object syncObject = new();
    private StorageObject.GoshujinClass storages = new();  // lock(syncObject)
    private StorageObject? currentStorage; // lock(syncObject)
    private int storageRotationCount; // lock(syncObject)

    #endregion

    public string[] GetInformation()
    {
        lock (this.syncObject)
        {
            return this.storages.Select(x => x.ToString()).ToArray();
        }
    }

    public bool CheckStorageId(ushort id) => this.storages.StorageIdChain.ContainsKey(id);

    public bool DeleteStorage(ushort id)
    {// Dev stage
        lock (this.syncObject)
        {
            if (!this.storages.StorageIdChain.TryGetValue(id, out var storageAndFiler))
            {// Not found
                return false;
            }

            // Move files to other storage...

            // Deletion
            storageAndFiler.Goshujin = null;

            return true;
        }
    }

    public (AddStorageResult Result, ushort Id) AddStorage_SimpleLocal(string path, long capacity)
    {
        if (capacity < 0)
        {
            capacity = DefaultStorageCapacity;
        }

        path = path.TrimEnd('\\');
        if (File.Exists(path))
        {
            return (AddStorageResult.WriteError, 0);
        }

        var relative = Path.GetRelativePath(this.Crystalizer.RootDirectory, path);
        if (!relative.StartsWith("..\\"))
        {
            path = relative;
        }

        var result = LocalFiler.Check(this.Crystalizer, path);
        if (result != AddStorageResult.Success)
        {
            return (result, 0);
        }

        ushort id;
        lock (this.syncObject)
        {
            id = this.GetFreeStorageId();
            var configuration = new SimpleStorageConfiguration(new LocalDirectoryConfiguration(path));

            var storageObject = new StorageObject(id, configuration);
            storageObject.StorageCapacity = capacity;
            if (storageObject.PrepareAndCheck(this, true).Result != CrystalResult.Success)
            {
                return (AddStorageResult.DuplicatePath, 0);
            }

            storageObject.Goshujin = this.storages;
        }

        return (AddStorageResult.Success, id);
    }

    public (AddStorageResult Result, ushort Id) AddStorage_SimpleS3(string bucket, string path, long capacity)
    {
        if (capacity < 0)
        {
            capacity = DefaultStorageCapacity;
        }

        path = path.TrimEnd('\\');
        var result = S3Filer.Check(this, bucket, path);
        if (result != AddStorageResult.Success)
        {
            return (result, 0);
        }

        ushort id;
        lock (this.syncObject)
        {
            id = this.GetFreeStorageId();

            var configuration = new SimpleStorageConfiguration(new S3DirectoryConfiguration(bucket, path));

            var storageObject = new StorageObject(id, configuration);
            storageObject.StorageCapacity = capacity;
            if (storageObject.PrepareAndCheck(this, true).Result != CrystalResult.Success)
            {
                return (AddStorageResult.DuplicatePath, 0);
            }

            storageObject.Goshujin = this.storages;
        }

        return (AddStorageResult.Success, id);
    }

    public async Task DeleteAllAsync()
    {
        var list = new List<Task>();
        lock (this.syncObject)
        {
            foreach (var x in this.storages)
            {
                if (x.Storage?.DeleteAllAsync() is { } task)
                {
                    list.Add(task);
                }
            }
        }

        await Task.WhenAll(list).ConfigureAwait(false);
    }

    public void Save(ref ushort storageId, ref ulong fileId, ByteArrayPool.ReadOnlyMemoryOwner memoryToBeShared, ushort datumId)
    {
        StorageObject? storage;
        lock (this.syncObject)
        {
            if (this.storages.StorageIdChain.Count == 0)
            {// No storage available.
                return;
            }
            else if (storageId == 0 || !this.storages.StorageIdChain.TryGetValue(storageId, out storage))
            {// Get valid directory.
                if (this.storageRotationCount >= StorageRotationThreshold ||
                    this.currentStorage == null)
                {
                    this.currentStorage = this.GetValidStorage();
                    this.storageRotationCount = memoryToBeShared.Memory.Length;
                    if (this.currentStorage == null)
                    {
                        return;
                    }
                }
                else
                {
                    this.storageRotationCount += memoryToBeShared.Memory.Length;
                }

                storage = this.currentStorage;
                storageId = storage.StorageId;
            }

            storage.MemoryStat.Add(memoryToBeShared.Memory.Length);
        }

        storage.Storage?.Put(ref fileId, memoryToBeShared);
    }

    public Task<CrystalMemoryOwnerResult> Load(ushort storageId, ulong fileId)
    {
        StorageObject? storageObject;
        lock (this.syncObject)
        {
            storageObject = this.GetStorageFromId(storageId);
            if (storageObject == null)
            {
                return Task.FromResult(new CrystalMemoryOwnerResult(CrystalResult.NoStorage));
            }
        }

        var task = storageObject.Storage?.GetAsync(ref fileId);
        if (task == null)
        {
            return Task.FromResult(new CrystalMemoryOwnerResult(CrystalResult.NoStorage));
        }

        return task;
    }

    public void Delete(ref ushort storageId, ref ulong fileId)
    {
        StorageObject? storageObject;
        lock (this.syncObject)
        {
            storageObject = this.GetStorageFromId(storageId);
            if (storageObject == null)
            {// No storage
                storageId = 0;
                fileId = 0;
                return;
            }
        }

        storageObject.Storage?.Delete(ref fileId);
    }

    internal async Task<CrystalSourceAndResult> PrepareAndCheck(StorageConfiguration configuration, CrystalPrepareParam param, ReadOnlyMemory<byte>? data)
    {// semaphore
        StorageObject.GoshujinClass? goshujin = null;
        if (data != null)
        {
            try
            {
                goshujin = TinyhandSerializer.Deserialize<StorageObject.GoshujinClass>(data.Value);
            }
            catch
            {
                var result = new CrystalSourceAndResult(CrystalSource.StorageGroup, CrystalResult.DeserializeError);
                if (await param.Query(result).ConfigureAwait(false) == AbortOrContinue.Abort)
                {
                    return result;
                }
            }
        }

        goshujin ??= TinyhandSerializer.Reconstruct<StorageObject.GoshujinClass>();
        List<StorageObject>? errorList = null;
        foreach (var x in goshujin.StorageIdChain)
        {
            var storageResult = await x.PrepareAndCheck(this, false).ConfigureAwait(false);
            if (storageResult != CrystalResult.Success)
            {
                errorList ??= new();
                errorList.Add(x);

                var result = new CrystalSourceAndResult(CrystalSource.StorageGroup, storageResult);
                if (await param.Query(result).ConfigureAwait(false) == AbortOrContinue.Abort)
                {
                    return result;
                }
            }
        }

        if (errorList != null)
        {
            foreach (var x in errorList)
            {
                x.Goshujin = null;
            }
        }

        if (goshujin.StorageIdChain.Count == 0)
        {
            try
            {
                var id = this.GetFreeStorageId();
                var storageObject = new StorageObject(id, configuration);
                await storageObject.PrepareAndCheck(this, true).ConfigureAwait(false);
                storageObject.Goshujin = goshujin;
            }
            catch
            {
            }
        }

        if (goshujin.StorageIdChain.Count == 0)
        {
            return new(CrystalSource.StorageGroup, CrystalResult.NoStorage);
        }

        lock (this.syncObject)
        {
            this.storages.Clear();
            this.storages = goshujin;
            this.currentStorage = null;
        }

        return CrystalSourceAndResult.Success;
    }

    internal async Task SaveStorage()
    {// Save
        Task[] tasks;
        lock (this.syncObject)
        {
            tasks = this.storages.Select(x => x.Save()).ToArray();
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    internal async Task SaveGroup(IFiler filer)
    {// Save storage information
        byte[] byteArray;
        lock (this.syncObject)
        {
            byteArray = TinyhandSerializer.Serialize(this.storages);
        }

        await PathHelper.SaveData(this.Crystalizer, byteArray, filer, 0).ConfigureAwait(false);
    }

    internal void Clear()
    {
        lock (this.syncObject)
        {
            this.storages.Clear();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private StorageObject? GetStorageFromId(ushort storageId)
    {// lock (this.syncObject)
        if (storageId == 0)
        {
            return null;
        }

        this.storages.StorageIdChain.TryGetValue(storageId, out var storageObject);
        return storageObject;
    }

    private ushort GetFreeStorageId()
    {// lock(syncObject)
        while (true)
        {
            var id = (ushort)RandomVault.Pseudo.NextUInt32();
            if (id != 0 && !this.storages.StorageIdChain.ContainsKey(id))
            {
                return id;
            }
        }
    }

    private StorageObject? GetValidStorage()
    {// lock(syncObject)
        var array = this.storages.StorageIdChain.ToArray();
        if (array == null)
        {
            return null;
        }

        return array.MinBy(a => a.GetUsageRatio());
    }
}
