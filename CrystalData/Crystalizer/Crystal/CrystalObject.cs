﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CrystalData.Filer;
using CrystalData.Storage;
using Tinyhand.IO;

namespace CrystalData;

public sealed class CrystalObject<TData> : ICrystalInternal<TData>, ITreeObject
    where TData : class, ITinyhandSerialize<TData>, ITinyhandReconstruct<TData>
{// Data + Journal/Waypoint + Filer/FileConfiguration + Storage/StorageConfiguration
    internal CrystalObject(Crystalizer crystalizer)
    {
        this.Crystalizer = crystalizer;
        this.CrystalConfiguration = CrystalConfiguration.Default;
        ((ITreeObject)this).TreeRoot = this;
    }

    #region FieldAndProperty

    private SemaphoreLock semaphore = new();
    private TData? data;
    private CrystalFiler? crystalFiler;
    private IStorageObsolete? storage;
    private Waypoint waypoint;
    private DateTime lastSavedTime;

    #endregion

    #region ICrystal

    public Crystalizer Crystalizer { get; }

    public CrystalConfiguration CrystalConfiguration { get; private set; }

    public Type DataType => typeof(TData);

    object ICrystal.Data => ((ICrystal<TData>)this).Data!;

    public TData Data
    {
        get
        {
            if (this.data is { } v)
            {
                return v;
            }

            using (this.semaphore.Lock())
            {
                if (this.State == CrystalState.Initial)
                {// Initial
                    this.PrepareAndLoadInternal(false).Wait();
                }
                else if (this.State == CrystalState.Deleted)
                {// Deleted
                    TinyhandSerializer.ReconstructObject<TData>(ref this.data);
                    this.SetJournal();
                    return this.data;
                }

                if (this.data != null)
                {
                    return this.data;
                }

                // Finally, reconstruct
                this.ResetWaypoint(true);
                return this.data;
            }
        }
    }

    public CrystalState State { get; private set; }

    /*public IFiler Filer
    {
        get
        {
            if (this.rawFiler is { } v)
            {
                return v;
            }

            using (this.semaphore.Lock())
            {
                if (this.rawFiler != null)
                {
                    return this.rawFiler;
                }

                this.ResolveAndPrepareFiler();
                return this.rawFiler;
            }
        }
    }*/

    public IStorageObsolete Storage
    {
        get
        {
            if (this.storage is { } v)
            {
                return v;
            }

            using (this.semaphore.Lock())
            {
                if (this.storage != null)
                {
                    return this.storage;
                }

                this.ResolveAndPrepareStorage();
                return this.storage;
            }
        }
    }

    void ICrystal.Configure(CrystalConfiguration configuration)
    {
        using (this.semaphore.Lock())
        {
            if (this.Crystalizer.DefaultBackup is { } globalBackup)
            {
                if (configuration.BackupFileConfiguration == null)
                {
                    configuration = configuration with { BackupFileConfiguration = globalBackup.CombineFile(configuration.FileConfiguration.Path) };
                }

                if (configuration.StorageConfiguration.BackupDirectoryConfiguration == null)
                {
                    var storageConfiguration = configuration.StorageConfiguration with { BackupDirectoryConfiguration = globalBackup.CombineDirectory(configuration.StorageConfiguration.DirectoryConfiguration), };
                    configuration = configuration with { StorageConfiguration = storageConfiguration, };
                }
            }

            this.CrystalConfiguration = configuration;
            this.crystalFiler = null;
            this.storage = null;
            this.State = CrystalState.Initial;
        }
    }

    void ICrystal.ConfigureFile(FileConfiguration configuration)
    {
        using (this.semaphore.Lock())
        {
            this.CrystalConfiguration = this.CrystalConfiguration with { FileConfiguration = configuration, };
            this.crystalFiler = null;
            this.State = CrystalState.Initial;
        }
    }

    void ICrystal.ConfigureStorage(StorageConfiguration configuration)
    {
        using (this.semaphore.Lock())
        {
            this.CrystalConfiguration = this.CrystalConfiguration with { StorageConfiguration = configuration, };
            this.storage = null;
            this.State = CrystalState.Initial;
        }
    }

    async Task<CrystalResult> ICrystal.PrepareAndLoad(bool useQuery)
    {
        using (this.semaphore.Lock())
        {
            if (this.State == CrystalState.Prepared)
            {// Prepared
                return CrystalResult.Success;
            }
            else if (this.State == CrystalState.Deleted)
            {// Deleted
                return CrystalResult.Deleted;
            }

            return await this.PrepareAndLoadInternal(useQuery).ConfigureAwait(false);
        }
    }

    async Task<CrystalResult> ICrystal.Save(UnloadMode unloadMode)
    {
        if (this.CrystalConfiguration.SavePolicy == SavePolicy.Volatile)
        {// Volatile
            if (unloadMode.IsUnload())
            {// Unload
                using (this.semaphore.Lock())
                {
                    this.data = null;
                    this.State = CrystalState.Initial;
                }
            }

            return CrystalResult.Success;
        }

        var obj = Volatile.Read(ref this.data);
        var filer = Volatile.Read(ref this.crystalFiler);
        var currentWaypoint = this.waypoint;

        if (this.State == CrystalState.Initial)
        {// Initial
            return CrystalResult.NotPrepared;
        }
        else if (this.State == CrystalState.Deleted)
        {// Deleted
            return CrystalResult.Deleted;
        }
        else if (obj == null || filer == null)
        {
            return CrystalResult.NotPrepared;
        }

        var semaphore = obj as IGoshujinSemaphore;
        if (semaphore is not null)
        {
            if (unloadMode == UnloadMode.TryUnload)
            {
                semaphore.LockAndTryUnload(out var state);
                if (state == GoshujinState.Valid)
                {// Cannot unload because a WriterClass is still present.
                    return CrystalResult.DataIsLocked;
                }
                else if (state == GoshujinState.Unloading)
                {// Unload (Success)
                    if (semaphore.SemaphoreCount > 0)
                    {
                        return CrystalResult.DataIsLocked;
                    }
                }
                else
                {// Obsolete
                    return CrystalResult.DataIsObsolete;
                }
            }
            else if (unloadMode == UnloadMode.ForceUnload)
            {
                semaphore.LockAndForceUnload();
            }
        }

        if (this.storage is { } storage && storage is not EmptyStorage)
        {
            await storage.SaveStorage().ConfigureAwait(false);
        }

        this.lastSavedTime = DateTime.UtcNow;

        // Starting position
        var startingPosition = this.Crystalizer.GetJournalPosition();

        // Serialize
        byte[] byteArray;
        var options = unloadMode == UnloadMode.NoUnload ? TinyhandSerializerOptions.Standard : TinyhandSerializerOptions.Unload;
        if (this.CrystalConfiguration.SaveFormat == SaveFormat.Utf8)
        {
            byteArray = TinyhandSerializer.SerializeObjectToUtf8(obj, options);
        }
        else
        {
            byteArray = TinyhandSerializer.SerializeObject(obj, options);
        }

        // Get hash
        var hash = FarmHash.Hash64(byteArray.AsSpan());
        if (hash == currentWaypoint.Hash)
        {// Identical data
            goto Exit;
        }

        var waypoint = this.waypoint;
        if (!waypoint.Equals(currentWaypoint))
        {// Waypoint changed
            goto Exit;
        }

        this.Crystalizer.UpdateWaypoint(this, ref currentWaypoint, hash);

        var result = await filer.Save(byteArray, currentWaypoint).ConfigureAwait(false);
        if (result != CrystalResult.Success)
        {// Write error
            return result;
        }

        using (this.semaphore.Lock())
        {// Update waypoint and plane position.
            this.waypoint = currentWaypoint;
            this.Crystalizer.CrystalCheck.SetShortcutPosition(currentWaypoint, startingPosition);
            if (unloadMode.IsUnload())
            {// Unload
                this.data = null;
                this.State = CrystalState.Initial;
            }
        }

        _ = filer.LimitNumberOfFiles();
        return CrystalResult.Success;

Exit:
        using (this.semaphore.Lock())
        {
            this.Crystalizer.CrystalCheck.SetShortcutPosition(currentWaypoint, startingPosition);
            if (unloadMode.IsUnload())
            {// Unload
                this.data = null;
                this.State = CrystalState.Initial;
            }
        }

        return CrystalResult.Success;
    }

    async Task<CrystalResult> ICrystal.Delete()
    {
        using (this.semaphore.Lock())
        {
            if (this.State == CrystalState.Initial)
            {// Initial
                await this.PrepareAndLoadInternal(false).ConfigureAwait(false);
            }
            else if (this.State == CrystalState.Deleted)
            {// Deleted
                return CrystalResult.Success;
            }

            // Delete file
            this.ResolveAndPrepareFiler();
            await this.crystalFiler.DeleteAllAsync().ConfigureAwait(false);

            // Delete storage
            this.ResolveAndPrepareStorage();
            await this.storage.DeleteStorageAsync().ConfigureAwait(false);

            // Journal/Waypoint
            this.Crystalizer.RemovePlane(this.waypoint);
            this.waypoint = default;

            // Clear
            TinyhandSerializer.DeserializeObject(TinyhandSerializer.SerializeObject(TinyhandSerializer.ReconstructObject<TData>()), ref this.data);
            // this.obj = default;
            // TinyhandSerializer.ReconstructObject<TData>(ref this.obj);

            this.State = CrystalState.Deleted;
        }

        this.Crystalizer.DeleteInternal(this);
        return CrystalResult.Success;
    }

    void ICrystal.Terminate()
    {
    }

    Task? ICrystalInternal.TryPeriodicSave(DateTime utc)
    {
        if (this.CrystalConfiguration.SavePolicy != SavePolicy.Periodic)
        {
            return null;
        }

        var elapsed = utc - this.lastSavedTime;
        if (elapsed < this.CrystalConfiguration.SaveInterval)
        {
            return null;
        }

        this.lastSavedTime = utc;
        return ((ICrystal)this).Save(UnloadMode.NoUnload);
    }

    Waypoint ICrystalInternal.Waypoint
        => this.waypoint;

    ITreeRoot? ITreeObject.TreeRoot { get; set; }

    ITreeObject? ITreeObject.TreeParent { get; set; } = null;

    int ITreeObject.TreeKey { get; set; } = -1;

    /*void ITreeObject.WriteLocator(ref Tinyhand.IO.TinyhandWriter writer)
    {
        writer.Write_Locator();
        writer.Write(this.waypoint.Plane);
    }*/

    async Task<bool> ICrystalInternal.TestJournal()
    {
        if (this.Crystalizer.Journal is not CrystalData.Journal.SimpleJournal journal)
        {// No journaling
            return true;
        }

        var testResult = true;
        using (this.semaphore.Lock())
        {
            if (this.crystalFiler is null ||
                this.crystalFiler.Main is not { } main)
            {
                return testResult;
            }

            var waypoints = main.GetWaypoints();
            if (waypoints.Length <= 1)
            {// The number of waypoints is 1 or less.
                return testResult;
            }

            var logger = this.Crystalizer.UnitLogger.GetLogger<TData>();
            TData? previousObject = default;
            for (var i = 0; i < waypoints.Length; i++)
            {// waypoint[i] -> waypoint[i + 1]
                var base32 = waypoints[i].ToBase32();

                // Load
                var result = await main.LoadWaypoint(waypoints[i]).ConfigureAwait(false);
                if (result.IsFailure)
                {// Loading error
                    logger.TryGet(LogLevel.Error)?.Log(CrystalDataHashed.TestJournal.LoadingFailure, base32);
                    testResult = false;
                    break;
                }

                // Deserialize
                (var currentObject, var currentFormat) = TryDeserialize(result.Data.Span, this.CrystalConfiguration.SaveFormat);
                if (currentObject is null)
                {// Deserialization error
                    result.Return();
                    logger.TryGet(LogLevel.Error)?.Log(CrystalDataHashed.TestJournal.DeserializationFailure, base32);
                    testResult = false;
                    break;
                }

                if (previousObject is not null)
                {// Compare the previous data
                    bool compare;
                    if (currentFormat == SaveFormat.Binary)
                    {// Previous (previousObject), Current (currentObject/result.Data.Span): Binary
                        compare = result.Data.Span.SequenceEqual(TinyhandSerializer.Serialize(previousObject));
                    }
                    else
                    {// Previous (previousObject), Current (currentObject/result.Data.Span): Utf8
                        compare = result.Data.Span.SequenceEqual(TinyhandSerializer.SerializeToUtf8(previousObject));
                    }

                    if (compare)
                    {// Success
                        logger.TryGet(LogLevel.Information)?.Log(CrystalDataHashed.TestJournal.Success, base32);
                    }
                    else
                    {// Failure
                        logger.TryGet(LogLevel.Error)?.Log(CrystalDataHashed.TestJournal.Failure, base32);
                        testResult = false;
                    }
                }

                result.Return();
                if (i == waypoints.Length - 1)
                {
                    break;
                }

                if (currentObject is not ITreeObject journalObject)
                {
                    break;
                }

                // journalObject.CurrentPlane = waypoints[i].CurrentPlane;

                // Read journal [waypoints[i].JournalPosition, waypoints[i + 1].JournalPosition)
                var length = (int)(waypoints[i + 1].JournalPosition - waypoints[i].JournalPosition);
                var memoryOwner = ByteArrayPool.Default.Rent(length).ToMemoryOwner(0, length);
                var journalResult = await journal.ReadJournalAsync(waypoints[i].JournalPosition, waypoints[i + 1].JournalPosition, memoryOwner.Memory).ConfigureAwait(false);
                if (!journalResult)
                {// Journal error
                    testResult = false;
                    break;
                }

                this.ReadJournal(journalObject, memoryOwner.Memory, waypoints[i].Plane);

                previousObject = currentObject;
            }
        }

        return testResult;
    }

    #endregion

    #region ITreeRoot

    bool ITreeRoot.TryGetJournalWriter(JournalType recordType, out TinyhandWriter writer)
    {
        if (this.Crystalizer.Journal is not null)
        {
            this.Crystalizer.Journal.GetWriter(recordType, out writer);

            writer.Write_Locator();
            writer.Write(this.waypoint.Plane);
            return true;
        }
        else
        {
            writer = default;
            return false;
        }
    }

    ulong ITreeRoot.AddJournal(in TinyhandWriter writer)
    {
        if (this.Crystalizer.Journal is not null)
        {
            return this.Crystalizer.Journal.Add(writer);
        }
        else
        {
            return 0;
        }
    }

    bool ITreeRoot.TryAddToSaveQueue()
    {
        if (this.CrystalConfiguration.SavePolicy == SavePolicy.OnChanged)
        {
            this.Crystalizer.AddToSaveQueue(this);
            return true;
        }
        else
        {
            return false;
        }
    }

    ulong ICrystal.AddStartingPoint()
    {
        if (this.Crystalizer.Journal is not null)
        {
            return this.Crystalizer.Journal.AddStartingPoint();
        }
        else
        {
            return 0;
        }
    }

    #endregion

    private static (TData? Data, SaveFormat Format) TryDeserialize(ReadOnlySpan<byte> span, SaveFormat formatHint)
    {
        TData? data = default;
        SaveFormat format = SaveFormat.Binary;

        if (span.Length == 0)
        {// Empty
            data = TinyhandSerializer.ReconstructObject<TData>();
            return (data, format);
        }

        if (formatHint == SaveFormat.Utf8)
        {
            try
            {
                TinyhandSerializer.DeserializeObjectFromUtf8(span, ref data);
                format = SaveFormat.Utf8;
            }
            catch
            {// Maybe binary...
                data = default;
                try
                {
                    TinyhandSerializer.DeserializeObject(span, ref data);
                }
                catch
                {
                    data = default;
                }
            }
        }
        else
        {
            try
            {
                TinyhandSerializer.DeserializeObject(span, ref data);
            }
            catch
            {// Maybe utf8...
                data = default;
                try
                {
                    TinyhandSerializer.DeserializeObjectFromUtf8(span, ref data);
                    format = SaveFormat.Utf8;
                }
                catch
                {
                    data = default;
                }
            }
        }

        return (data, format);
    }

    private bool ReadJournal(ITreeObject journalObject, ReadOnlyMemory<byte> data, uint currentPlane)
    {
        var reader = new TinyhandReader(data.Span);
        var success = true;

        while (reader.Consumed < data.Length)
        {
            if (!reader.TryReadRecord(out var length, out var journalType))
            {
                return false;
            }

            var fork = reader.Fork();
            try
            {
                if (journalType == JournalType.Record)
                {
                    reader.Read_Locator();
                    var plane = reader.ReadUInt32();

                    if (plane == currentPlane)
                    {
                        if (journalObject.ReadRecord(ref reader))
                        {// Success
                        }
                        else
                        {// Failure
                            success = false;
                        }
                    }
                }
                else
                {
                }
            }
            catch
            {
                success = false;
            }
            finally
            {
                reader = fork;
                reader.Advance(length);
            }
        }

        return success;
    }

    private async Task<CrystalResult> PrepareAndLoadInternal(bool useQuery)
    {// this.semaphore.Lock()
        CrystalResult result;
        var param = PrepareParam.New<TData>(this.Crystalizer, useQuery);

        // CrystalFiler
        if (this.crystalFiler == null)
        {
            this.crystalFiler = new(this.Crystalizer);
            result = await this.crystalFiler.PrepareAndCheck(param, this.CrystalConfiguration).ConfigureAwait(false);
            if (result.IsFailure())
            {
                return result;
            }
        }

        // Storage
        if (this.storage == null)
        {
            this.storage = this.Crystalizer.ResolveStorage(this.CrystalConfiguration.StorageConfiguration);
            result = await this.storage.PrepareAndCheck(param, this.CrystalConfiguration.StorageConfiguration, false).ConfigureAwait(false);
            if (result.IsFailure())
            {
                return result;
            }
        }

        // Data
        if (this.data is not null)
        {
            this.State = CrystalState.Prepared;
            return CrystalResult.Success;
        }

        var filer = Volatile.Read(ref this.crystalFiler);
        var configuration = this.CrystalConfiguration;

        // !!! EXIT !!!
        this.semaphore.Exit();
        (CrystalResult Result, TData? Data, Waypoint Waypoint) loadResult;
        try
        {
            loadResult = await LoadAndDeserializeNotInternal(filer, param, configuration).ConfigureAwait(false);
        }
        finally
        {
            this.semaphore.Enter();
        }

        // !!! ENTERED !!!
        if (this.data is not null)
        {
            return CrystalResult.Success;
        }
        else if (loadResult.Result.IsFailure())
        {
            return loadResult.Result;
        }

        // Check journal position
        if (loadResult.Waypoint.IsValid && this.Crystalizer.Journal is { } journal)
        {
            if (loadResult.Waypoint.JournalPosition > journal.GetCurrentPosition())
            {
                var query = await param.Query.InconsistentJournal(this.CrystalConfiguration.FileConfiguration.Path).ConfigureAwait(false);
                if (query == AbortOrContinue.Abort)
                {
                    return CrystalResult.CorruptedData;
                }
                else
                {
                    journal.ResetJournal(loadResult.Waypoint.JournalPosition);
                }
            }
        }

        if (loadResult.Data is { } data)
        {// Loaded
            this.data = data;
            this.waypoint = loadResult.Waypoint;
            if (this.CrystalConfiguration.HasFileHistories)
            {
                if (this.waypoint.IsValid)
                {// Valid waypoint
                    this.Crystalizer.SetPlane(this, ref this.waypoint);
                    this.SetJournal();
                }
                else
                {// Invalid waypoint
                    this.ResetWaypoint(false);
                }
            }

            // this.LogWaypoint("Load");
            this.State = CrystalState.Prepared;
            return CrystalResult.Success;
        }
        else
        {// Reconstruct
            this.ResetWaypoint(true);

            // this.LogWaypoint("Reconstruct");
            this.State = CrystalState.Prepared;
            return CrystalResult.Success;
        }
    }

#pragma warning disable SA1204 // Static elements should appear before instance elements
    private static async Task<(CrystalResult Result, TData? Data, Waypoint Waypoint)> LoadAndDeserializeNotInternal(CrystalFiler filer, PrepareParam param, CrystalConfiguration configuration)
#pragma warning restore SA1204 // Static elements should appear before instance elements
    {
        param.RegisterConfiguration(configuration.FileConfiguration, out var newlyRegistered);

        // Load data
        var data = await filer.LoadLatest(param).ConfigureAwait(false);
        if (data.Result.IsFailure)
        {
            if (!newlyRegistered &&
                configuration.RequiredForLoading &&
                await param.Query.FailedToLoad(configuration.FileConfiguration, data.Result.Result).ConfigureAwait(false) == AbortOrContinue.Abort)
            {
                return (data.Result.Result, default, default);
            }

            return (CrystalResult.Success, default, default); // Reconstruct
        }

        // Deserialize
        try
        {
            var deserializeResult = TryDeserialize(data.Result.Data.Memory.Span, configuration.SaveFormat);
            if (deserializeResult.Data == null)
            {
                if (configuration.RequiredForLoading &&
                    await param.Query.FailedToLoad(configuration.FileConfiguration, CrystalResult.DeserializeError).ConfigureAwait(false) == AbortOrContinue.Abort)
                {
                    return (data.Result.Result, default, default);
                }

                return (CrystalResult.Success, default, default); // Reconstruct
            }

            return (CrystalResult.Success, deserializeResult.Data, data.Waypoint);
        }
        finally
        {
            data.Result.Return();
        }
    }

    [MemberNotNull(nameof(crystalFiler))]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResolveAndPrepareFiler()
    {
        if (this.crystalFiler == null)
        {
            this.crystalFiler = new(this.Crystalizer);
            this.crystalFiler.PrepareAndCheck(PrepareParam.NoQuery<TData>(this.Crystalizer), this.CrystalConfiguration).Wait();
        }
    }

    [MemberNotNull(nameof(storage))]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResolveAndPrepareStorage()
    {
        if (this.storage == null)
        {
            this.storage = this.Crystalizer.ResolveStorage(this.CrystalConfiguration.StorageConfiguration);
            this.storage.PrepareAndCheck(PrepareParam.NoQuery<TData>(this.Crystalizer), this.CrystalConfiguration.StorageConfiguration, false).Wait();
        }
    }

    [MemberNotNull(nameof(data))]
    private void ResetWaypoint(bool reconstructObject)
    {
        if (reconstructObject || this.data is null)
        {
            TinyhandSerializer.ReconstructObject<TData>(ref this.data);
        }

        byte[] byteArray;
        if (this.CrystalConfiguration.SaveFormat == SaveFormat.Utf8)
        {
            byteArray = TinyhandSerializer.SerializeObjectToUtf8(this.data);
        }
        else
        {
            byteArray = TinyhandSerializer.SerializeObject(this.data);
        }

        var hash = FarmHash.Hash64(byteArray);
        this.waypoint = default;
        this.Crystalizer.UpdateWaypoint(this, ref this.waypoint, hash);

        this.SetJournal();

        // Save immediately to fix the waypoint.
        _ = this.crystalFiler?.Save(byteArray, this.waypoint);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetJournal()
    {
        if (this.data is ITreeObject journalObject)
        {
            // journalObject.Journal = this;
            journalObject.SetParent(this);
        }
    }

    private void LogWaypoint(string prefix)
    {
        var logger = this.Crystalizer.UnitLogger.GetLogger<TData>();
        logger.TryGet(LogLevel.Error)?.Log($"{prefix}, {this.waypoint.ToString()}");
    }
}
