// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.IO;
using System.Runtime.CompilerServices;

namespace ZenItz;

public enum ZenDirectoryType
{
    Standard,
}

[TinyhandObject]
[ValueLinkObject]
internal partial class ZenDirectory
{
    public const int DefaultMaxSnowflakeSize = 1024 * 1024 * 1024; // 1GB = 4MB x 256
    public const int HashSize = 8;

    [Link(Primary = true, Name = "List", Type = ChainType.List)]
    public ZenDirectory()
    {
    }

    public ZenDirectory(uint directoryId, string path)
    {
        this.DirectoryId = directoryId;
        this.DirectoryPath = path;
    }

    public ZenDirectoryInformation GetInformation()
    {
        this.CalculateUsageRatio();
        return new(this.DirectoryId, this.Type, this.DirectoryPath, this.DirectoryCapacity, this.DirectorySize, this.UsageRatio);
    }

    internal async Task<ZenDataResult> Load(ulong file)
    {
        Snowflake? snowflake;
        var snowflakeId = ZenFile.ToSnowflakeId(file);
        int size = 0;

        lock (this.snowflakeGoshujin)
        {
            if (snowflakeId != 0 &&
                this.snowflakeGoshujin.SnowflakeIdChain.TryGetValue(snowflakeId, out snowflake))
            {// Found
                size = snowflake.Size;
            }
            else
            {// Not found
                return new(ZenResult.NoFile);
            }
        }

        // Load (snowflakeId, size)
        if (this.worker != null)
        {
            var workInterface = this.worker.AddLast(new(snowflake.SnowflakeId, size));
            if (await workInterface.WaitForCompletionAsync().ConfigureAwait(false) == true)
            {// Complete
                var data = workInterface.Work.LoadData;
                if (data.IsRent)
                {// Success
                    return new(ZenResult.Success, data.AsReadOnly());
                }
                else
                {// Failure
                    return new(ZenResult.NoFile);
                }
            }
            else
            {// Abort
                return new(ZenResult.NoFile);
            }
        }

        return new(ZenResult.NotStarted);
    }

    internal void Save(ref ulong file, ByteArrayPool.ReadOnlyMemoryOwner memoryOwner)
    {// DirectoryId: valid, SnowflakeId: ?
        Snowflake? snowflake;
        var snowflakeId = ZenFile.ToSnowflakeId(file);
        var dataSize = memoryOwner.Memory.Length;

        lock (this.snowflakeGoshujin)
        {
            if (snowflakeId != 0 &&
                this.snowflakeGoshujin.SnowflakeIdChain.TryGetValue(snowflakeId, out snowflake))
            {// Found
                if (dataSize > snowflake.Size)
                {
                    this.DirectorySize += dataSize - snowflake.Size;
                }

                snowflake.Size = dataSize;
            }
            else
            {// Not found
                snowflake = this.GetNewSnowflake();
                this.DirectorySize += dataSize; // Forget about hash size.
                snowflake.Size = dataSize;
            }

            file = ZenFile.ToFile(this.DirectoryId, snowflake.SnowflakeId);
        }

        this.worker?.AddLast(new(snowflake.SnowflakeId, memoryOwner.IncrementAndShare()));
    }

    internal void Remove(ulong file)
    {
        Snowflake? snowflake;
        var snowflakeId = ZenFile.ToSnowflakeId(file);

        lock (this.snowflakeGoshujin)
        {
            if (snowflakeId != 0 &&
                this.snowflakeGoshujin.SnowflakeIdChain.TryGetValue(snowflakeId, out snowflake))
            {// Found
                snowflake.Goshujin = null;
            }
            else
            {// Not found
                return;
            }
        }

        // Load (snowflakeId, size)
        if (this.worker != null)
        {
            this.worker.AddLast(new(snowflake.SnowflakeId));
        }
    }

    internal bool PrepareAndCheck(string rootDirectory)
    {
        try
        {
            if (Path.IsPathRooted(this.DirectoryPath))
            {
                this.RootedPath = this.DirectoryPath;
            }
            else
            {
                this.RootedPath = Path.Combine(rootDirectory, this.DirectoryPath);
            }

            Directory.CreateDirectory(this.RootedPath);
            /*var directoryInfo = new DirectoryInfo(this.DirectoryPath);
            if (createDirectory)
            {
                directoryInfo.Create();
            }
            else
            {
                if (!directoryInfo.Exists)
                {// No directory

                }
            }*/

            /*var testFile = Path.Combine(this.DirectoryPath, Path.GetRandomFileName());
            using (var fs = File.Create(testFile, 1, FileOptions.DeleteOnClose))
            {
            }*/

            // Check directory file
            using (var handle = File.OpenHandle(this.DirectoryFile, mode: FileMode.Open, access: FileAccess.ReadWrite))
            {
            }
        }
        catch
        {// No directory file
            return false;
        }

        return true;
    }

    internal void Start()
    {
        if (this.worker != null)
        {
            return;
        }

        // Directory.CreateDirectory(this.DirectoryPath);

        if (!this.TryLoadDirectory(this.DirectoryFile))
        {
            this.TryLoadDirectory(this.DirectoryBackup);
        }

        this.worker = new ZenDirectoryWorker(ThreadCore.Root, this);
    }

    internal async Task WaitForCompletionAsync()
    {
        if (this.worker != null)
        {
            await this.worker.WaitForCompletionAsync();
        }
    }

    internal async Task StopAsync()
    {
        if (this.worker != null)
        {
            await this.worker.WaitForCompletionAsync();
            this.worker.Dispose();
            this.worker = null;
        }

        await this.SaveDirectoryAsync(this.DirectoryFile, this.DirectoryBackup);
    }

    [Key(0)]
    [Link(Type = ChainType.Unordered)]
    public uint DirectoryId { get; private set; }

    [Key(1)]
    public ZenDirectoryType Type { get; private set; }

    [Key(2)]
    [Link(Type = ChainType.Unordered)]
    public string DirectoryPath { get; private set; } = string.Empty;

    [Key(3)]
    public long DirectoryCapacity { get; internal set; }

    [Key(4)]
    public long DirectorySize { get; private set; }

    [IgnoreMember]
    public string RootedPath { get; private set; } = string.Empty;

    public string DirectoryFile => Path.Combine(this.RootedPath, Zen.DefaultDirectoryFile);

    public string DirectoryBackup => Path.Combine(this.RootedPath, Zen.DefaultDirectoryBackup);

    [IgnoreMember]
    internal double UsageRatio { get; private set; }

    internal void CalculateUsageRatio()
    {
        if (this.DirectoryCapacity == 0)
        {
            this.UsageRatio = 0;
            return;
        }

        var ratio = (double)this.DirectorySize / (double)this.DirectoryCapacity;
        if (ratio < 0)
        {
            ratio = 0;
        }
        else if (ratio > 1)
        {
            ratio = 1;
        }

        this.UsageRatio = ratio;
    }

    internal (string Directory, string File) GetSnowflakePath(uint snowflakeId)
    {
        Span<char> c = stackalloc char[2];
        Span<char> d = stackalloc char[6];

        c[0] = this.UInt32ToChar(snowflakeId >> 28);
        c[1] = this.UInt32ToChar(snowflakeId >> 24);

        d[0] = this.UInt32ToChar(snowflakeId >> 20);
        d[1] = this.UInt32ToChar(snowflakeId >> 16);
        d[2] = this.UInt32ToChar(snowflakeId >> 12);
        d[3] = this.UInt32ToChar(snowflakeId >> 8);
        d[4] = this.UInt32ToChar(snowflakeId >> 4);
        d[5] = this.UInt32ToChar(snowflakeId);

        return (c.ToString(), d.ToString());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private char UInt32ToChar(uint x)
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

    private bool TryLoadDirectory(string path)
    {
        byte[] file;
        try
        {
            file = File.ReadAllBytes(path);
        }
        catch
        {
            return false;
        }

        if (!HashHelper.CheckFarmHashAndGetData(file.AsMemory(), out var data))
        {
            return false;
        }

        try
        {
            var g = TinyhandSerializer.Deserialize<Snowflake.GoshujinClass>(data);
            if (g != null)
            {
                this.snowflakeGoshujin = g;
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    private Task<bool> SaveDirectoryAsync(string path, string? backupPath = null)
    {
        byte[] data;
        lock (this.snowflakeGoshujin)
        {
            data = TinyhandSerializer.Serialize(this.snowflakeGoshujin);
        }

        return HashHelper.GetFarmHashAndSaveAsync(data, path, backupPath);
    }

    private Snowflake GetNewSnowflake()
    {// lock (this.snoflakeGoshujin)
        while (true)
        {
            var id = LP.Random.Pseudo.NextUInt32();
            if (id != 0 && !this.snowflakeGoshujin.SnowflakeIdChain.ContainsKey(id))
            {
                var snowflake = new Snowflake(id);
                this.snowflakeGoshujin.Add(snowflake);
                return snowflake;
            }
        }
    }

    private Snowflake.GoshujinClass snowflakeGoshujin = new();
    private ZenDirectoryWorker? worker;
}
