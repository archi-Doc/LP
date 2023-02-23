﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

#pragma warning disable SA1124 // Do not use regions

namespace CrystalData;

public enum CrystalDirectoryType
{
    Standard,
}

[TinyhandObject]
[ValueLinkObject]
internal partial class CrystalDirectory : IDisposable
{
    public const int HashSize = 8;

    [Link(Primary = true, Name = "List", Type = ChainType.List)]
    public CrystalDirectory()
    {
        this.worker = new CrystalDirectoryWorker(ThreadCore.Root, this);
    }

    internal CrystalDirectory(uint directoryId, string path)
        : base()
    {
        this.worker = new CrystalDirectoryWorker(ThreadCore.Root, this);
        this.DirectoryId = directoryId;
        this.DirectoryPath = path;
    }

    public CrystalDirectoryInformation GetInformation()
    {
        this.CalculateUsageRatio();
        return new(this.DirectoryId, this.Type, this.DirectoryPath, this.DirectoryCapacity, this.DirectorySize, this.UsageRatio);
    }

    internal async Task<CrystalMemoryOwnerResult> Load(ulong file)
    {
        Snowflake? snowflake;
        var snowflakeId = CrystalHelper.ToSnowflakeId(file);
        int size = 0;

        lock (this.syncObject)
        {
            if (snowflakeId != 0 &&
                this.snowflakeGoshujin.SnowflakeIdChain.TryGetValue(snowflakeId, out snowflake) &&
                snowflake.IsAlive)
            {// Found
                size = snowflake.Size;
            }
            else
            {// Not found
                return new(CrystalResult.NoFile);
            }
        }

        // Load (snowflakeId, size)
        var workInterface = this.worker.AddLast(new(snowflake.SnowflakeId, size));
        if (await workInterface.WaitForCompletionAsync().ConfigureAwait(false) == true)
        {// Complete
            var data = workInterface.Work.LoadData;
            if (data.IsRent)
            {// Success
                return new(CrystalResult.Success, data.AsReadOnly());
            }
            else
            {// Failure
                return new(CrystalResult.NoFile);
            }
        }
        else
        {// Abort
            return new(CrystalResult.NoFile);
        }
    }

    internal void Save(ref ulong file, ByteArrayPool.ReadOnlyMemoryOwner memoryToBeShared)
    {// DirectoryId: valid, SnowflakeId: ?
        Snowflake? snowflake;
        var snowflakeId = CrystalHelper.ToSnowflakeId(file);
        var dataSize = memoryToBeShared.Memory.Length;

        lock (this.syncObject)
        {
            if (snowflakeId != 0 &&
                this.snowflakeGoshujin.SnowflakeIdChain.TryGetValue(snowflakeId, out snowflake) &&
                snowflake.IsAlive)
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
                this.DirectorySize += dataSize; // Forget about the hash size.
                snowflake.Size = dataSize;
            }

            file = CrystalHelper.ToFile(this.DirectoryId, snowflake.SnowflakeId);
        }

        this.worker.AddLast(new(snowflake.SnowflakeId, memoryToBeShared.IncrementAndShare()));
    }

    internal void Delete(ulong file)
    {
        var snowflakeId = CrystalHelper.ToSnowflakeId(file);
        lock (this.syncObject)
        {
            if (snowflakeId != 0 &&
                this.snowflakeGoshujin.SnowflakeIdChain.TryGetValue(snowflakeId, out var snowflake))
            {// Found
                snowflake.MarkForDeletion();
            }
            else
            {// Not found
                return;
            }
        }

        // Remove (snowflakeId)
        this.worker.AddLast(new(snowflakeId));
    }

    internal bool PrepareAndCheck(Storage io)
    {
        this.Options = io.Options;
        try
        {
            if (Path.IsPathRooted(this.DirectoryPath))
            {
                this.RootedPath = this.DirectoryPath;
            }
            else
            {
                this.RootedPath = Path.Combine(this.Options.RootPath, this.DirectoryPath);
            }

            Directory.CreateDirectory(this.RootedPath);

            // Check directory file
            try
            {
                using (var handle = File.OpenHandle(this.SnowflakeFilePath, mode: FileMode.Open, access: FileAccess.ReadWrite))
                {
                }
            }
            catch
            {
                using (var handle = File.OpenHandle(this.SnowflakeBackupPath, mode: FileMode.Open, access: FileAccess.ReadWrite))
                {
                }
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
        if (!this.TryLoadDirectory(this.SnowflakeFilePath))
        {
            this.TryLoadDirectory(this.SnowflakeBackupPath);
        }
    }

    internal async Task WaitForCompletionAsync()
    {
        await this.worker.WaitForCompletionAsync();
    }

    internal async Task StopAsync()
    {
        await this.worker.WaitForCompletionAsync();
        await this.SaveDirectoryAsync(this.SnowflakeFilePath, this.SnowflakeBackupPath);
    }

    [Key(0)]
    [Link(Type = ChainType.Unordered)]
    public uint DirectoryId { get; private set; }

    [Key(1)]
    public CrystalDirectoryType Type { get; private set; }

    [Key(2)]
    [Link(Type = ChainType.Unordered)]
    public string DirectoryPath { get; private set; } = string.Empty;

    [Key(3)]
    public long DirectoryCapacity { get; internal set; }

    [Key(4)]
    public long DirectorySize { get; private set; } // lock (this.syncObject)

    [IgnoreMember]
    public CrystalOptions Options { get; private set; } = CrystalOptions.Default;

    [IgnoreMember]
    public string RootedPath { get; private set; } = string.Empty;

    public string SnowflakeFilePath => Path.Combine(this.RootedPath, this.Options.SnowflakeFile);

    public string SnowflakeBackupPath => Path.Combine(this.RootedPath, this.Options.SnowflakeBackup);

    [IgnoreMember]
    internal double UsageRatio { get; private set; }

    internal void CalculateUsageRatio()
    {
        if (this.DirectoryCapacity == 0)
        {
            this.UsageRatio = 0;
            return;
        }

        var ratio = (double)this.DirectorySize / this.DirectoryCapacity;
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

    internal void RemoveSnowflake(uint snowflakeId)
    {
        lock (this.syncObject)
        {
            if (this.snowflakeGoshujin.SnowflakeIdChain.TryGetValue(snowflakeId, out var snowflake))
            {
                snowflake.Goshujin = null;
            }
        }
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
        lock (this.syncObject)
        {
            data = TinyhandSerializer.Serialize(this.snowflakeGoshujin);
        }

        return HashHelper.GetFarmHashAndSaveAsync(data, path, backupPath);
    }

    private Snowflake GetNewSnowflake()
    {// lock (this.syncObject)
        while (true)
        {
            var id = LP.Random.Pseudo.NextUInt32();
            if (id != 0 && !this.snowflakeGoshujin.SnowflakeIdChain.ContainsKey(id))
            {
                var snowflake = new Snowflake(id);
                snowflake.Goshujin = this.snowflakeGoshujin;
                return snowflake;
            }
        }
    }

    private object syncObject = new();
    private Snowflake.GoshujinClass snowflakeGoshujin = new(); // lock (this.syncObject)
    // private Dictionary<uint, Snowflake> dictionary = new(); // lock (this.syncObject)
    private CrystalDirectoryWorker worker;

    #region IDisposable Support
#pragma warning restore SA1124 // Do not use regions

    private bool disposed = false; // To detect redundant calls.

    /// <summary>
    /// Finalizes an instance of the <see cref="CrystalDirectory"/> class.
    /// </summary>
    ~CrystalDirectory()
    {
        this.Dispose(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// free managed/native resources.
    /// </summary>
    /// <param name="disposing">true: free managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed)
        {
            if (disposing)
            {
                // free managed resources.
                this.worker.Dispose();
            }

            // free native resources here if there are any.
            this.disposed = true;
        }
    }
    #endregion
}