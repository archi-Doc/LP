﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Amazon.S3.Model.Internal.MarshallTransformations;
using CrystalData.Check;
using CrystalData.Filer;
using CrystalData.Journal;
using CrystalData.Storage;
using CrystalData.UserInterface;
using Tinyhand.IO;

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
        this.GlobalMain = options.GlobalMain;
        this.GlobalBackup = options.GlobalBackup;
        this.EnableFilerLogger = options.EnableFilerLogger;
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

    public DirectoryConfiguration GlobalMain { get; }

    public DirectoryConfiguration? GlobalBackup { get; }

    public bool EnableFilerLogger { get; }

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

    /*public (IFiler Filer, PathConfiguration FixedConfiguration) ResolveFiler(PathConfiguration configuration)
    {
        var resolved = this.ResolveRawFiler(configuration);
        return (new RawFilerToFiler(this, resolved.RawFiler, resolved.FixedConfiguration.Path), resolved.FixedConfiguration);
    }*/

    public (IFiler Filer, FileConfiguration FixedConfiguration) ResolveFiler(FileConfiguration configuration)
    {
        var resolved = this.ResolveRawFiler(configuration);
        return (new RawFilerToFiler(this, resolved.RawFiler, resolved.FixedConfiguration.Path), resolved.FixedConfiguration);
    }

    public (IFiler Filer, DirectoryConfiguration FixedConfiguration) ResolveFiler(DirectoryConfiguration configuration)
    {
        var resolved = this.ResolveRawFiler(configuration);
        return (new RawFilerToFiler(this, resolved.RawFiler, resolved.FixedConfiguration.Path), resolved.FixedConfiguration);
    }

    /*public (IRawFiler RawFiler, PathConfiguration FixedConfiguration) ResolveRawFiler(PathConfiguration configuration)
    {
        lock (this.syncFiler)
        {
            if (configuration is RelativeFileConfiguration)
            {// Relative file
                configuration = this.GlobalMain.CombineFile(configuration.Path);
            }
            else if (configuration is RelativeDirectoryConfiguration directoryConfiguration)
            {// Relative directory
                configuration = this.GlobalMain.CombineDirectory(directoryConfiguration);
            }

            if (configuration is EmptyFileConfiguration ||
                configuration is EmptyDirectoryConfiguration)
            {// Empty file or directory
                return (EmptyFiler.Default, configuration);
            }
            else if (configuration is LocalFileConfiguration ||
                configuration is LocalDirectoryConfiguration)
            {// Local file or directory
                if (this.localFiler == null)
                {
                    this.localFiler ??= new LocalFiler();
                }

                return (this.localFiler, configuration);
            }
            else if (configuration is S3FileConfiguration s3FilerConfiguration)
            {// S3 file
                return (ResolveS3Filer(s3FilerConfiguration.Bucket), configuration);
            }
            else if (configuration is S3DirectoryConfiguration s3DirectoryConfiguration)
            {// S3 directory
                return (ResolveS3Filer(s3DirectoryConfiguration.Bucket), configuration);
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
    }*/

    public (IRawFiler RawFiler, FileConfiguration FixedConfiguration) ResolveRawFiler(FileConfiguration configuration)
    {
        lock (this.syncFiler)
        {
            if (configuration is RelativeFileConfiguration)
            {// Relative file
                configuration = this.GlobalMain.CombineFile(configuration.Path);
            }

            if (configuration is EmptyFileConfiguration)
            {// Empty file
                return (EmptyFiler.Default, configuration);
            }
            else if (configuration is LocalFileConfiguration)
            {// Local file
                if (this.localFiler == null)
                {
                    this.localFiler ??= new LocalFiler();
                }

                return (this.localFiler, configuration);
            }
            else if (configuration is S3FileConfiguration s3Configuration)
            {// S3 file
                if (!this.bucketToS3Filer.TryGetValue(s3Configuration.Bucket, out var filer))
                {
                    filer = new S3Filer(s3Configuration.Bucket);
                    this.bucketToS3Filer.TryAdd(s3Configuration.Bucket, filer);
                }

                return (filer, configuration);
            }
            else
            {
                ThrowConfigurationNotRegistered(configuration.GetType());
                return default!;
            }
        }
    }

    public (IRawFiler RawFiler, DirectoryConfiguration FixedConfiguration) ResolveRawFiler(DirectoryConfiguration configuration)
    {
        lock (this.syncFiler)
        {
            if (configuration is RelativeDirectoryConfiguration)
            {// Relative directory
                configuration = this.GlobalMain.CombineDirectory(configuration);
            }

            if (configuration is EmptyDirectoryConfiguration)
            {// Empty directory
                return (EmptyFiler.Default, configuration);
            }
            else if (configuration is LocalDirectoryConfiguration)
            {// Local directory
                if (this.localFiler == null)
                {
                    this.localFiler ??= new LocalFiler();
                }

                return (this.localFiler, configuration);
            }
            else if (configuration is S3DirectoryConfiguration s3Configuration)
            {// S3 directory
                if (!this.bucketToS3Filer.TryGetValue(s3Configuration.Bucket, out var filer))
                {
                    filer = new S3Filer(s3Configuration.Bucket);
                    this.bucketToS3Filer.TryAdd(s3Configuration.Bucket, filer);
                }

                return (filer, configuration);
            }
            else
            {
                ThrowConfigurationNotRegistered(configuration.GetType());
                return default!;
            }
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

        var resolved = this.ResolveFiler(configuration);
        var result = await resolved.Filer.PrepareAndCheck(PrepareParam.NoQuery<Crystalizer>(this), resolved.FixedConfiguration).ConfigureAwait(false);
        if (result.IsFailure())
        {
            return result;
        }

        var bytes = TinyhandSerializer.SerializeToUtf8(data);
        result = await resolved.Filer.WriteAsync(0, new(bytes)).ConfigureAwait(false);

        return result;
    }

    public async Task<CrystalResult> LoadConfigurations(FileConfiguration configuration)
    {
        var resolved = this.ResolveFiler(configuration);
        var result = await resolved.Filer.PrepareAndCheck(PrepareParam.NoQuery<Crystalizer>(this), resolved.FixedConfiguration).ConfigureAwait(false);
        if (result.IsFailure())
        {
            return result;
        }

        var readResult = await resolved.Filer.ReadAsync(0, -1).ConfigureAwait(false);
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
        // var list = new List<string>();
        foreach (var x in crystals)
        {
            result = await x.PrepareAndLoad(useQuery).ConfigureAwait(false);
            if (result.IsFailure())
            {
                return result;
            }

            // list.Add(x.Data.GetType().Name);
        }

        // Dump Plane
        // this.DumpPlane();

        // Read journal
        await this.ReadJournal().ConfigureAwait(false);

        // Save crystal check
        this.CrystalCheck.Save();
        this.CrystalCheck.ClearPlanePosition();

        // this.logger.TryGet()?.Log($"Prepared - {string.Join(", ", list)}");

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
        await this.SaveAndTerminate(true, true);
    }

    public async Task SaveJournalOnlyForTest()
    {
        await this.SaveAndTerminate(false, true);
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

    public ICrystal<TData> CreateCrystal<TData>(bool managedByCrystalizer = true)
        where TData : class, ITinyhandSerialize<TData>, ITinyhandReconstruct<TData>
    {
        var crystal = new CrystalObject<TData>(this);
        if (managedByCrystalizer)
        {
            this.crystals.TryAdd(crystal, 0);
        }

        if (this.configuration.CrystalConfigurations.TryGetValue(typeof(TData), out var configuration))
        {
            ((ICrystal)crystal).Configure(configuration);
        }

        return crystal;
    }

    public ICrystal<TData> GetOrCreateCrystal<TData>(CrystalConfiguration configuration)
        where TData : class, ITinyhandSerialize<TData>, ITinyhandReconstruct<TData>
    {
        if (this.typeToCrystal.TryGetValue(typeof(TData), out var crystal) &&
            crystal is ICrystal<TData> crystalData)
        {
            return crystalData;
        }

        var crystalObject = new CrystalObject<TData>(this);
        this.crystals.TryAdd(crystalObject, 0);
        ((ICrystal)crystalObject).Configure(configuration);
        return crystalObject;
    }

    public ICrystal<TData> CreateBigCrystal<TData>(bool managedByCrystalizer = true)
        where TData : BaseData, ITinyhandSerialize<TData>, ITinyhandReconstruct<TData>
    {
        var crystal = new BigCrystalObject<TData>(this);
        if (managedByCrystalizer)
        {
            this.crystals.TryAdd(crystal, 0);
        }

        if (this.configuration.CrystalConfigurations.TryGetValue(typeof(TData), out var configuration))
        {
            ((ICrystal)crystal).Configure(configuration);
        }

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

    public async Task TestJournalAll()
    {
        var crystals = this.crystals.Keys.ToArray();
        foreach (var x in crystals)
        {
            await x.TestJournal().ConfigureAwait(false);
        }
    }

    #endregion

    #region Waypoint/Plane

    internal void UpdateWaypoint(ICrystalInternal crystal, ref Waypoint waypoint, ulong hash, ulong startingPosition)
    {
        var plane = waypoint.Plane;
        if (plane == 0)
        {
            while (true)
            {
                plane = RandomVault.Pseudo.NextUInt32();
                if (plane != 0 && this.planeToCrystal.TryAdd(plane, crystal))
                {// Success
                    break;
                }
            }
        }

        // Add journal
        ulong journalPosition;
        if (this.Journal != null)
        {
            this.Journal.GetWriter(JournalType.Waypoint, out var writer);
            writer.Write(plane);
            writer.Write(hash);
            journalPosition = this.Journal.Add(writer);
        }
        else
        {
            journalPosition = waypoint.JournalPosition + 1;
        }

        waypoint = new(journalPosition, plane, hash);
    }

    /*internal void UpdatePlane(ICrystalInternal crystal, ref Waypoint waypoint, ulong hash, ulong startingPosition)
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
        ulong depth = 0;
        if (this.Journal != null)
        {
            this.Journal.GetWriter(JournalType.Waypoint, out var writer);
            writer.Write(newPlane);
            writer.Write(hash);
            journalPosition = this.Journal.Add(writer);

            depth = journalPosition - startingPosition;
            if (depth < 0 || depth > Waypoint.MaxDepth || startingPosition == 0)
            {
                depth = 0;
            }
        }
        else
        {
            journalPosition = waypoint.JournalPosition + 1;
        }

        waypoint = new(journalPosition, nextPlane, newPlane, hash, (uint)depth);
    }*/

    internal void RemovePlane(Waypoint waypoint)
    {
        if (waypoint.Plane != 0)
        {
            this.planeToCrystal.TryRemove(waypoint.Plane, out _);
            // this.CrystalCheck.TryRemovePlane(waypoint.CurrentPlane);
        }
    }

    internal void SetPlane(ICrystalInternal crystal, ref Waypoint waypoint)
    {
        if (waypoint.Plane != 0)
        {
            this.planeToCrystal[waypoint.Plane] = crystal;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ulong AddStartingPoint(uint plane)
    {
        if (this.Journal is { } journal)
        {
            journal.GetWriter(JournalType.Startingpoint, out var writer);
            return journal.Add(writer);
        }
        else
        {
            return 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ulong GetJournalPosition()
    {
        if (this.Journal is { } journal)
        {
            return journal.GetCurrentPosition();
        }
        else
        {
            return 0;
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
        if (!this.typeToCrystal.TryGetValue(crystal.DataType, out _))
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

        return crystal!.Data;
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

    private async Task SaveAndTerminate(bool saveData, bool saveJournal)
    {
        if (saveData)
        {
            await this.SaveAll(true).ConfigureAwait(false);
        }

        this.CrystalCheck.Save();

        // Save/Terminate journal
        if (this.Journal is { } journal)
        {
            if (saveJournal)
            {
                await journal.SaveJournalAsync().ConfigureAwait(false);
            }

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

    #endregion

    #region Journal

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
                if (this.GlobalBackup is { } globalBackup)
                {
                    if (simpleJournalConfiguration.BackupDirectoryConfiguration == null)
                    {
                        simpleJournalConfiguration = simpleJournalConfiguration with
                        {
                            BackupDirectoryConfiguration = globalBackup.CombineDirectory(simpleJournalConfiguration.DirectoryConfiguration),
                        };
                    }
                }

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

    private async Task ReadJournal()
    {
        if (this.Journal is { } journal)
        {// Load journal
            ulong position = journal.GetCurrentPosition();

            var array = this.planeToCrystal.ToArray();
            for (var i = 0; i < array.Length; i++)
            {
                var waypoint = array[i].Value.Waypoint;
                if (!this.CrystalCheck.TryGetPlanePosition(waypoint.Plane, out var planePosition))
                {
                    this.logger.TryGet(LogLevel.Error)?.Log($"No plane position: {array[i].Value.DataType.Name}");
                }

                var max = planePosition > waypoint.JournalPosition ? planePosition : waypoint.JournalPosition;
                array[i].Value.JournalPosition = max;
                if (position > max)
                {
                    position = max;
                }

                // this.logger.TryGet(LogLevel.Debug)?.Log($"JournalPosition {array[i].Value.DataType.Name}: {max}");
            }

            var startPosition = position;
            ulong endPosition = 0;
            while (position != 0)
            {
                endPosition = position;
                var journalResult = await journal.ReadJournalAsync(position).ConfigureAwait(false);
                if (journalResult.NextPosition == 0)
                {
                    break;
                }

                try
                {
                    this.ProcessJournal(position, journalResult.Data.Memory);
                }
                finally
                {
                    journalResult.Data.Return();
                }

                position = journalResult.NextPosition;
            }

            this.logger.TryGet(LogLevel.Debug)?.Log($"Journal read {startPosition} - {endPosition}");
            foreach (var x in this.crystals.Keys)
            {// Update journal position
                x.JournalPosition = endPosition;
            }
        }
    }

    private void ProcessJournal(ulong position, Memory<byte> data)
    {
        var reader = new TinyhandReader(data.Span);
        var success = false;
        var failure = false;

        while (reader.Consumed < data.Length)
        {
            if (!reader.TryReadRecord(out var length, out var journalType))
            {
                this.logger.TryGet(LogLevel.Error)?.Log(CrystalDataHashed.Journal.Corrupted);
                return;
            }

            var fork = reader.Fork();
            try
            {
                if (journalType == JournalType.Startingpoint)
                {
                }
                else if (journalType == JournalType.Waypoint)
                {
                    reader.ReadUInt32();
                    reader.ReadUInt64();
                }
                else if (journalType == JournalType.Record)
                {
                    reader.Read_Locator();
                    var plane = reader.ReadUInt32();
                    if (this.planeToCrystal.TryGetValue(plane, out var crystal))
                    {
                        if (crystal.Data is IJournalObject journalObject)
                        {
                            var currentPosition = position + (ulong)reader.Consumed;
                            if (currentPosition > crystal.JournalPosition)
                            {
                                if (journalObject.ReadRecord(ref reader))
                                {// Success
                                 // this.logger.TryGet(LogLevel.Debug)?.Log($"Journal read, Plane: {plane}, Length: {length} => {crystal.GetType().FullName}");
                                    success = true;
                                }
                                else
                                {// Failure
                                    failure = true;
                                }
                            }
                        }
                    }
                }
                else
                {
                }
            }
            catch
            {
            }
            finally
            {
                reader = fork;
                if (!reader.TryAdvance(length))
                {
                    failure = true;
                    reader.TryAdvance(reader.Remaining);
                }
            }
        }

        if (failure)
        {
            this.logger.TryGet(LogLevel.Error)?.Log(CrystalDataHashed.Journal.ReadFailure);
        }
        else if (success)
        {
            this.logger.TryGet(LogLevel.Error)?.Log(CrystalDataHashed.Journal.ReadSuccess);
        }
    }

    #endregion

    private void DumpPlane()
    {
        foreach (var x in this.planeToCrystal)
        {
            this.logger.TryGet(LogLevel.Debug)?.Log($"Plane: {x.Key} = {x.Value.GetType().FullName}");
        }
    }
}
