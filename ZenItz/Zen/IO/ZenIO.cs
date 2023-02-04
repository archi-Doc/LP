﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using LPEssentials;
using ZenItz.Results;

namespace ZenItz;

public sealed class ZenIO
{
    public const int DirectoryRotationThreshold = 1024 * 1024 * 100; // 100 MB

    internal ZenIO()
    {
    }

    public ZenDirectoryInformation[] GetDirectoryInformation()
    {
        lock (this.syncObject)
        {
            return this.directoryGoshujin.Select(a => a.GetInformation()).ToArray();
        }
    }

    public AddDictionaryResult AddDirectory(string path, uint id = 0, long capacity = ZenOptions.DefaultDirectoryCapacity)
    {
        if (capacity < 0)
        {
            capacity = ZenOptions.DefaultDirectoryCapacity;
        }

        if (path.EndsWith('\\'))
        {
            path = path.Substring(0, path.Length - 1);
        }

        if (File.Exists(path))
        {
            return AddDictionaryResult.FileExists;
        }

        var relative = Path.GetRelativePath(this.Options.RootPath, path);
        if (!relative.StartsWith("..\\"))
        {
            path = relative;
        }

        lock (this.syncObject)
        {
            if (this.Started)
            {
                return AddDictionaryResult.ZenRunning;
            }

            if (id == 0)
            {
                id = this.GetFreeDirectoryId(this.directoryGoshujin);
            }

            var directory = new ZenDirectory(id, path);
            directory.DirectoryCapacity = capacity;

            if (this.directoryGoshujin.DirectoryIdChain.ContainsKey(id))
            {
                return AddDictionaryResult.DuplicateId;
            }
            else if (this.directoryGoshujin.DirectoryPathChain.ContainsKey(path))
            {
                return AddDictionaryResult.DuplicatePath;
            }

            this.directoryGoshujin.Add(directory);
        }

        return AddDictionaryResult.Success;
    }

    public void RemoveAll()
    {
        string[] directories;
        lock (this.syncObject)
        {
            directories = this.directoryGoshujin.Select(x => x.RootedPath).ToArray();
        }

        foreach (var x in directories)
        {
            PathHelper.TryDeleteDirectory(x);
        }
    }

    public ZenOptions Options { get; private set; } = ZenOptions.Default;

    public bool Started { get; private set; }

    internal void Save(ref ulong file, ByteArrayPool.ReadOnlyMemoryOwner memoryOwner)
    {
        ZenDirectory? directory;
        if (this.directoryGoshujin.DirectoryIdChain.Count == 0)
        {// No directory available.
            return;
        }
        else if (!ZenFile.IsValidFile(file) || !this.directoryGoshujin.DirectoryIdChain.TryGetValue(ZenFile.ToDirectoryId(file), out directory))
        {// Get valid directory.
            if (this.directoryRotationCount >= DirectoryRotationThreshold ||
                this.currentDirectory == null)
            {
                this.currentDirectory = this.GetDirectory();
                Volatile.Write(ref this.directoryRotationCount, memoryOwner.Memory.Length);
                if (this.currentDirectory == null)
                {
                    return;
                }
            }
            else
            {
                Interlocked.Add(ref this.directoryRotationCount, memoryOwner.Memory.Length);
            }

            directory = this.currentDirectory;
        }

        directory.Save(ref file, memoryOwner);
    }

    internal async Task<ZenMemoryOwnerResult> Load(ulong file)
    {
        ZenDirectory? directory;
        if (!ZenFile.IsValidFile(file))
        {// Invalid file.
            return new(ZenResult.NoData);
        }
        else if (!this.directoryGoshujin.DirectoryIdChain.TryGetValue(ZenFile.ToDirectoryId(file), out directory))
        {// No directory
            return new(ZenResult.NoDirectory);
        }

        return await directory.Load(file);
    }

    internal void Remove(ulong file)
    {
        ZenDirectory? directory;
        if (!ZenFile.IsValidFile(file))
        {// Invalid file.
            return;
        }

        lock (this.syncObject)
        {
            if (!this.directoryGoshujin.DirectoryIdChain.TryGetValue(ZenFile.ToDirectoryId(file), out directory))
            {// No directory
                return;
            }
        }

        directory.Remove(file);
    }

    internal void Restart()
    {
        lock (this.syncObject)
        {
            if (this.Started)
            {
                return;
            }

            foreach (var x in this.directoryGoshujin)
            {
                x.PrepareAndCheck(this);
                x.Start();
            }
        }

        this.Started = true;
    }

    internal async Task<ZenStartResult> TryStart(ZenOptions options, ZenStartParam param, ReadOnlyMemory<byte>? data)
    {
        if (this.Started)
        {
            return ZenStartResult.Success;
        }

        this.Options = options;

        ZenDirectory.GoshujinClass? goshujin = null;
        if (data != null)
        {
            try
            {
                goshujin = TinyhandSerializer.Deserialize<ZenDirectory.GoshujinClass>(data.Value);
            }
            catch
            {
                if (!await param.Query(ZenStartResult.ZenFileError))
                {
                    return ZenStartResult.ZenFileError;
                }
            }
        }

        goshujin ??= new();
        List<string>? errorDirectories = null;
        foreach (var x in goshujin)
        {
            if (!x.PrepareAndCheck(this))
            {
                errorDirectories ??= new();
                errorDirectories.Add(x.DirectoryPath);
            }
        }

        if (errorDirectories != null &&
            !await param.Query(ZenStartResult.ZenDirectoryError, errorDirectories.ToArray()))
        {
            return ZenStartResult.ZenFileError;
        }

        if (goshujin.DirectoryIdChain.Count == 0)
        {
            try
            {
                var defaultDirectory = new ZenDirectory(this.GetFreeDirectoryId(goshujin), PathHelper.GetRootedDirectory(this.Options.RootPath, this.Options.DefaultZenDirectory));
                defaultDirectory.PrepareAndCheck(this);
                goshujin.Add(defaultDirectory);
            }
            catch
            {
            }
        }

        foreach (var x in goshujin)
        {
            x.Start();
        }

        if (goshujin.DirectoryIdChain.Count == 0)
        {
            return ZenStartResult.NoDirectoryAvailable;
        }

        lock (this.syncObject)
        {
            this.directoryGoshujin = goshujin;
            this.Started = true;
        }

        return ZenStartResult.Success;
    }

    internal async Task WaitForCompletionAsync()
    {
        Task[] tasks;
        lock (this.syncObject)
        {
            tasks = this.directoryGoshujin.Select(x => x.WaitForCompletionAsync()).ToArray();
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    internal async Task StopAsync()
    {
        Task[] tasks;
        lock (this.syncObject)
        {
            if (!this.Started)
            {
                return;
            }

            tasks = this.directoryGoshujin.Select(x => x.StopAsync()).ToArray();
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        this.Started = false;
    }

    internal byte[] Serialize()
    {
        return TinyhandSerializer.Serialize(this.directoryGoshujin);
    }

    private uint GetFreeDirectoryId(ZenDirectory.GoshujinClass goshujin)
    {// lock(syncObject)
        while (true)
        {
            var id = LP.Random.Pseudo.NextUInt32();
            if (id != 0 && !goshujin.DirectoryIdChain.ContainsKey(id))
            {
                return id;
            }
        }
    }

    private ZenDirectory? GetDirectory()
    {
        var array = this.directoryGoshujin.ListChain.ToArray();
        if (array == null)
        {
            return null;
        }

        foreach (var x in array)
        {
            x.CalculateUsageRatio();
        }

        return array.MinBy(a => a.UsageRatio);
    }

    private object syncObject = new();
    private ZenDirectory.GoshujinClass directoryGoshujin = new(); // lock(syncObject)
    private ZenDirectory? currentDirectory;
    private int directoryRotationCount;
}
