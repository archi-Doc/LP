﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using CrystalData.Check;
using CrystalData.Filer;
using CrystalData.Journal;
using CrystalData.Storage;
using CrystalData.UserInterface;

#pragma warning disable SA1204

namespace CrystalData;

public class Crystalizer
{
    public const string CheckFile = "Crystal.check";
    public const int TaskIntervalInMilliseconds = 1_000;
    public const int PeriodicSaveInMilliseconds = 10_000;

    private class CrystalizerTask : TaskCore
    {
        public CrystalizerTask(Crystalizer crystalizer)
            : base(null, Process)
        {
            this.crystalizer = crystalizer;
        }

        private static async Task Process(object? parameter)
        {
            var core = (CrystalizerTask)parameter!;
            int elapsedMilliseconds = 0;
            while (await core.Delay(TaskIntervalInMilliseconds).ConfigureAwait(false))
            {
                await core.crystalizer.QueuedSave();

                elapsedMilliseconds += TaskIntervalInMilliseconds;
                if (elapsedMilliseconds >= PeriodicSaveInMilliseconds)
                {
                    elapsedMilliseconds = 0;
                    await core.crystalizer.PeriodicSave();
                }
            }
        }

        private Crystalizer crystalizer;
    }

    public Crystalizer(CrystalizerConfiguration configuration, CrystalizerOptions options, ICrystalDataQuery query, ILogger<Crystalizer> logger, UnitLogger unitLogger, IStorageKey storageKey)
    {
        this.configuration = configuration;
        this.GlobalBackup = options.GlobalBackup;
        this.EnableLogger = options.EnableLogger;
        this.RootDirectory = options.RootPath;
        this.DefaultTimeout = options.DefaultTimeout;
        this.MemorySizeLimit = options.MemorySizeLimit;
        this.MaxParentInMemory = options.MaxParentInMemory;
        if (string.IsNullOrEmpty(this.RootDirectory))
        {
            this.RootDirectory = Directory.GetCurrentDirectory();
        }

        this.logger = logger;
        this.task = new(this);
        this.Query = query;
        this.QueryContinue = new CrystalDataQueryNo();
        this.UnitLogger = unitLogger;
        this.CrystalCheck = new(this.UnitLogger.GetLogger<CrystalCheck>());
        this.CrystalCheck.Load(Path.Combine(this.RootDirectory, CheckFile));
        this.Himo = new(this);
        this.StorageKey = storageKey;

        foreach (var x in this.configuration.CrystalConfigurations)
        {
            ICrystalInternal? crystal;
            if (x.Value is BigCrystalConfiguration bigCrystalConfiguration)
            {// new BigCrystalImpl<TData>
                var bigCrystal = (IBigCrystalInternal)Activator.CreateInstance(typeof(BigCrystalObject<>).MakeGenericType(x.Key), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { this, }, null)!;
                crystal = bigCrystal;
                bigCrystal.Configure(bigCrystalConfiguration);
            }
            else
            {// new CrystalImpl<TData>
                crystal = (ICrystalInternal)Activator.CreateInstance(typeof(CrystalObject<>).MakeGenericType(x.Key), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { this, }, null)!;
                crystal.Configure(x.Value);
            }

            this.typeToCrystal.TryAdd(x.Key, crystal);
            this.crystals.TryAdd(crystal, 0);
        }
    }

    #region FieldAndProperty

    public DirectoryConfiguration? GlobalBackup { get; }

    public bool EnableLogger { get; }

    public string RootDirectory { get; }

    public TimeSpan DefaultTimeout { get; }

    public long MemorySizeLimit { get; }

    public int MaxParentInMemory { get; }

    public IJournal? Journal { get; private set; }

    public IStorageKey StorageKey { get; }

    public HimoGoshujinClass Himo { get; }

    internal ICrystalDataQuery Query { get; }

    internal ICrystalDataQuery QueryContinue { get; }

    internal UnitLogger UnitLogger { get; }

    internal CrystalCheck CrystalCheck { get; }

    private CrystalizerConfiguration configuration;
    private ILogger logger;
    private CrystalizerTask task;
    private ThreadsafeTypeKeyHashTable<ICrystalInternal> typeToCrystal = new(); // Type to ICrystal
    private ConcurrentDictionary<ICrystalInternal, int> crystals = new(); // All crystals
    private ConcurrentDictionary<uint, ICrystalInternal> planeToCrystal = new(); // Plane to crystal
    private ConcurrentDictionary<ICrystal, int> saveQueue = new(); // Save queue

    private object syncFiler = new();
    private IRawFiler? localFiler;
    private Dictionary<string, IRawFiler> bucketToS3Filer = new();

    #endregion

    #region Resolvers

    public IFiler ResolveFiler(PathConfiguration configuration)
    {
        return new RawFilerToFiler(this, this.ResolveRawFiler(configuration), configuration.Path);
    }

    public IRawFiler ResolveRawFiler(PathConfiguration configuration)
    {
        lock (this.syncFiler)
        {
            if (configuration is EmptyFileConfiguration ||
                configuration is EmptyDirectoryConfiguration)
            {// Empty file or directory
                return EmptyFiler.Default;
            }
            else if (configuration is LocalFileConfiguration ||
                configuration is LocalDirectoryConfiguration)
            {// Local file or directory
                if (this.localFiler == null)
                {
                    this.localFiler ??= new LocalFiler();
                }

                return this.localFiler;
            }
            else if (configuration is S3FileConfiguration s3FilerConfiguration)
            {// S3 file
                return ResolveS3Filer(s3FilerConfiguration.Bucket);
            }
            else if (configuration is S3DirectoryConfiguration s3DirectoryConfiguration)
            {// S3 directory
                return ResolveS3Filer(s3DirectoryConfiguration.Bucket);
            }
            else
            {
                ThrowConfigurationNotRegistered(configuration.GetType());
                return default!;
            }
        }

        IRawFiler ResolveS3Filer(string bucket)
        {
            if (!this.bucketToS3Filer.TryGetValue(bucket, out var filer))
            {
                filer = new S3Filer(bucket);
                this.bucketToS3Filer.TryAdd(bucket, filer);
            }

            return filer;
        }
    }

    public IStorage ResolveStorage(StorageConfiguration configuration)
    {
        lock (this.syncFiler)
        {
            IStorage storage;
            if (configuration is EmptyStorageConfiguration emptyStorageConfiguration)
            {// Empty storage
                storage = EmptyStorage.Default;
            }
            else if (configuration is SimpleStorageConfiguration simpleStorageConfiguration)
            {
                storage = new SimpleStorage(this);
            }
            else
            {
                ThrowConfigurationNotRegistered(configuration.GetType());
                return default!;
            }

            storage.SetTimeout(this.DefaultTimeout);
            return storage;
        }
    }

    #endregion

    #region Main

    public void ResetConfigurations()
    {
        foreach (var x in this.configuration.CrystalConfigurations)
        {
            if (this.typeToCrystal.TryGetValue(x.Key, out var crystal))
            {
                if (x.Value is BigCrystalConfiguration bigCrystalConfiguration &&
                    crystal is IBigCrystal bigCrystal)
                {
                    bigCrystal.Configure(bigCrystalConfiguration);
                }
                else
                {
                    crystal.Configure(x.Value);
                }
            }
        }
    }

    public async Task<CrystalResult> SaveConfigurations(FileConfiguration configuration)
    {
        var data = TinyhandSerializer.ReconstructObject<CrystalizerConfigurationData>();
        foreach (var x in this.configuration.CrystalConfigurations)
        {
            if (this.typeToCrystal.TryGetValue(x.Key, out var crystal) &&
                x.Key.FullName is { } name)
            {
                if (crystal.CrystalConfiguration is BigCrystalConfiguration bigCrystalConfiguration)
                {
                    data.BigCrystalConfigurations[name] = bigCrystalConfiguration;
                }
                else
                {
                    data.CrystalConfigurations[name] = crystal.CrystalConfiguration;
                }
            }
        }

        var filer = this.ResolveFiler(configuration);
        var result = await filer.PrepareAndCheck(PrepareParam.NoQuery<Crystalizer>(this), configuration).ConfigureAwait(false);
        if (result.IsFailure())
        {
            return result;
        }

        var bytes = TinyhandSerializer.SerializeToUtf8(data);
        result = await filer.WriteAsync(0, new(bytes)).ConfigureAwait(false);

        return result;
    }

    public async Task<CrystalResult> LoadConfigurations(FileConfiguration configuration)
    {
        var filer = this.ResolveFiler(configuration);
        var result = await filer.PrepareAndCheck(PrepareParam.NoQuery<Crystalizer>(this), configuration).ConfigureAwait(false);
        if (result.IsFailure())
        {
            return result;
        }

        var readResult = await filer.ReadAsync(0, -1).ConfigureAwait(false);
        if (readResult.IsFailure)
        {
            return readResult.Result;
        }

        try
        {
            var data = TinyhandSerializer.DeserializeFromUtf8<CrystalizerConfigurationData>(readResult.Data.Memory);
            if (data == null)
            {
                return CrystalResult.DeserializeError;
            }

            var nameToCrystal = new Dictionary<string, ICrystal>();
            foreach (var x in this.typeToCrystal.ToArray())
            {
                if (x.Key.FullName is { } name)
                {
                    nameToCrystal[name] = x.Value;
                }
            }

            foreach (var x in data.CrystalConfigurations)
            {
                if (nameToCrystal.TryGetValue(x.Key, out var crystal))
                {
                    crystal.Configure(x.Value);
                }
            }

            foreach (var x in data.BigCrystalConfigurations)
            {
                if (nameToCrystal.TryGetValue(x.Key, out var crystal) &&
                    crystal is IBigCrystal bigCrystal)
                {
                    bigCrystal.Configure(x.Value);
                }
            }

            return CrystalResult.Success;
        }
        catch
        {
            return CrystalResult.DeserializeError;
        }
        finally
        {
            readResult.Return();
        }
    }

    public async Task<CrystalResult> PrepareAndLoadAll(bool useQuery = true)
    {
        // Check file
        if (!this.CrystalCheck.SuccessfullyLoaded)
        {
            if (await this.Query.NoCheckFile() == AbortOrContinue.Abort)
            {
                return CrystalResult.NotFound;
            }
            else
            {
                this.CrystalCheck.SuccessfullyLoaded = true;
            }
        }

        // Journal
        var result = await this.PrepareJournal(useQuery).ConfigureAwait(false);
        if (result.IsFailure())
        {
            return result;
        }

        // Crystals
        var crystals = this.crystals.Keys.ToArray();
        var list = new List<string>();
        foreach (var x in crystals)
        {
            result = await x.PrepareAndLoad(useQuery).ConfigureAwait(false);
            if (result.IsFailure())
            {
                return result;
            }

            list.Add(x.Object.GetType().Name);
        }

        this.logger.TryGet()?.Log($"Prepared - {string.Join(", ", list)}");

        return CrystalResult.Success;
    }

    public async Task SaveAll(bool unload = false)
    {
        this.CrystalCheck.Save();

        var crystals = this.crystals.Keys.ToArray();
        foreach (var x in crystals)
        {
            await x.Save(unload).ConfigureAwait(false);
        }
    }

    public async Task SaveAllAndTerminate()
    {
        await this.SaveAll(true).ConfigureAwait(false);

        // Terminate journal
        if (this.Journal is { } journal)
        {
            await journal.TerminateAsync().ConfigureAwait(false);
        }

        // Terminate filers/journal
        var tasks = new List<Task>();
        lock (this.syncFiler)
        {
            if (this.localFiler is not null)
            {
                tasks.Add(this.localFiler.TerminateAsync());
                this.localFiler = null;
            }

            foreach (var x in this.bucketToS3Filer.Values)
            {
                tasks.Add(x.TerminateAsync());
            }

            this.bucketToS3Filer.Clear();
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        this.logger.TryGet()?.Log($"Terminated - {this.Himo.MemoryUsage}");
    }

    public void AddToSaveQueue(ICrystal crystal)
    {
        this.saveQueue.TryAdd(crystal, 0);
    }

    public async Task<CrystalResult[]> DeleteAll()
    {
        var tasks = this.crystals.Keys.Select(x => x.Delete()).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    public ICrystal<TData> CreateCrystal<TData>()
        where TData : class, ITinyhandSerialize<TData>, ITinyhandReconstruct<TData>
    {
        var crystal = new CrystalObject<TData>(this);
        this.crystals.TryAdd(crystal, 0);
        return crystal;
    }

    public ICrystal<TData> CreateBigCrystal<TData>()
        where TData : BaseData, ITinyhandSerialize<TData>, ITinyhandReconstruct<TData>
    {
        var crystal = new BigCrystalObject<TData>(this);
        this.crystals.TryAdd(crystal, 0);
        return crystal;
    }

    public ICrystal<TData> GetCrystal<TData>()
        where TData : class, ITinyhandSerialize<TData>, ITinyhandReconstruct<TData>
    {
        if (!this.typeToCrystal.TryGetValue(typeof(TData), out var c) ||
            c is not ICrystal<TData> crystal)
        {
            ThrowTypeNotRegistered(typeof(TData));
            return default!;
        }

        return crystal;
    }

    public IBigCrystal<TData> GetBigCrystal<TData>()
        where TData : BaseData, ITinyhandSerialize<TData>, ITinyhandReconstruct<TData>
    {
        if (!this.typeToCrystal.TryGetValue(typeof(TData), out var c) ||
            c is not IBigCrystal<TData> crystal)
        {
            ThrowTypeNotRegistered(typeof(TData));
            return default!;
        }

        return crystal;
    }

    public async Task MergeJournalForTest()
    {
        if (this.Journal is SimpleJournal simpleJournal)
        {
            await simpleJournal.Merge(true);
        }
    }

    #endregion

    #region Waypoint/Plane

    internal void UpdatePlane(ICrystalInternal crystal, ref Waypoint waypoint, ulong hash)
    {
        if (waypoint.CurrentPlane != 0)
        {// Remove the current plane
            this.planeToCrystal.TryRemove(waypoint.CurrentPlane, out _);
        }

        // Next plane
        var nextPlane = waypoint.NextPlane;
        if (nextPlane == 0)
        {
            while (true)
            {
                nextPlane = RandomVault.Pseudo.NextUInt32();
                if (nextPlane != 0 && this.planeToCrystal.TryAdd(nextPlane, crystal))
                {// Success
                    break;
                }
            }
        }

        // New plane
        uint newPlane;
        while (true)
        {
            newPlane = RandomVault.Pseudo.NextUInt32();
            if (newPlane != 0 && this.planeToCrystal.TryAdd(newPlane, crystal))
            {// Success
                break;
            }
        }

        // Current/Next -> Next/New

        // Add journal
        ulong journalPosition;
        if (this.Journal != null)
        {
            this.Journal.GetWriter(JournalType.Waypoint, nextPlane, out var writer);
            writer.Write(newPlane);
            writer.Write(hash);
            journalPosition = this.Journal.Add(writer);
        }
        else
        {
            journalPosition = waypoint.JournalPosition + 1;
        }

        waypoint = new(journalPosition, nextPlane, newPlane, hash);
    }

    internal void RemovePlane(Waypoint waypoint)
    {
        if (waypoint.CurrentPlane != 0)
        {
            this.planeToCrystal.TryRemove(waypoint.CurrentPlane, out _);
        }

        if (waypoint.NextPlane != 0)
        {
            this.planeToCrystal.TryRemove(waypoint.NextPlane, out _);
        }
    }

    internal void SetPlane(ICrystalInternal crystal, ref Waypoint waypoint)
    {
        if (waypoint.CurrentPlane != 0)
        {
            this.planeToCrystal[waypoint.CurrentPlane] = crystal;
        }

        if (waypoint.NextPlane != 0)
        {
            this.planeToCrystal[waypoint.NextPlane] = crystal;
        }
    }

    #endregion

    #region Misc

    internal static string GetRootedFile(Crystalizer? crystalizer, string file)
        => crystalizer == null ? file : PathHelper.GetRootedFile(crystalizer.RootDirectory, file);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowTypeNotRegistered(Type type)
    {
        throw new InvalidOperationException($"The specified data type '{type.Name}' is not registered. Register the data type within ConfigureCrystal().");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowConfigurationNotRegistered(Type type)
    {
        throw new InvalidOperationException($"The specified configuration type '{type.Name}' is not registered.");
    }

    internal bool DeleteInternal(ICrystalInternal crystal)
    {
        if (!this.typeToCrystal.TryGetValue(crystal.ObjectType, out _))
        {// Created crystals
            return this.crystals.TryRemove(crystal, out _);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ICrystal GetCrystal(Type type)
    {
        if (!this.typeToCrystal.TryGetValue(type, out var crystal))
        {
            ThrowTypeNotRegistered(type);
        }

        return crystal!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal object GetObject(Type type)
    {
        if (!this.typeToCrystal.TryGetValue(type, out var crystal))
        {
            ThrowTypeNotRegistered(type);
        }

        return crystal!.Object;
    }

    /*internal CrystalConfiguration GetCrystalConfiguration(Type type)
    {
        if (!this.configuration.CrystalConfigurations.TryGetValue(type, out var configuration))
        {
            ThrowTypeNotRegistered(type);
        }

        return configuration!;
    }

    internal BigCrystalConfiguration GetBigCrystalConfiguration(Type type)
    {
        if (!this.configuration.CrystalConfigurations.TryGetValue(type, out var configuration))
        {
            ThrowTypeNotRegistered(type);
        }

        if (configuration is not BigCrystalConfiguration bigCrystalConfiguration)
        {
            ThrowTypeNotRegistered(type);
            return default!;
        }

        return bigCrystalConfiguration;
    }*/

    private async Task<CrystalResult> PrepareJournal(bool useQuery = true)
    {
        if (this.Journal == null)
        {// New journal
            var configuration = this.configuration.JournalConfiguration;
            if (configuration is EmptyJournalConfiguration)
            {
                return CrystalResult.Success;
            }
            else if (configuration is SimpleJournalConfiguration simpleJournalConfiguration)
            {
                var simpleJournal = new SimpleJournal(this, simpleJournalConfiguration, this.UnitLogger.GetLogger<SimpleJournal>());
                this.Journal = simpleJournal;
            }
            else
            {
                return CrystalResult.NotFound;
            }
        }

        return await this.Journal.Prepare(PrepareParam.New<Crystalizer>(this, useQuery)).ConfigureAwait(false);
    }

    private Task PeriodicSave()
    {
        var tasks = new List<Task>();
        var crystals = this.crystals.Keys.ToArray();
        var utc = DateTime.UtcNow;
        foreach (var x in crystals)
        {
            if (x.TryPeriodicSave(utc) is { } task)
            {
                tasks.Add(task);
            }
        }

        return Task.WhenAll(tasks);
    }

    private Task QueuedSave()
    {
        var tasks = new List<Task>();
        var array = this.saveQueue.Keys.ToArray();
        this.saveQueue.Clear();
        foreach (var x in array)
        {
            if (x.State == CrystalState.Prepared)
            {
                tasks.Add(x.Save(false));
            }
        }

        return Task.WhenAll(tasks);
    }

    #endregion
}
