﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using CrystalData.Filer;
using CrystalData.Journal;
using CrystalData.Storage;

#pragma warning disable SA1204

namespace CrystalData;

public class Crystalizer
{
    public Crystalizer(CrystalizerConfiguration configuration, CrystalizerOptions options, ILogger logger, UnitLogger unitLogger, IStorageKey storageKey)
    {
        this.configuration = configuration;
        this.EnableLogger = options.EnableLogger;
        this.RootDirectory = options.RootPath;
        this.DefaultTimeout = options.DefaultTimeout;
        if (string.IsNullOrEmpty(this.RootDirectory))
        {
            this.RootDirectory = Directory.GetCurrentDirectory();
        }

        this.logger = logger;
        this.UnitLogger = unitLogger;
        this.StorageKey = storageKey;

        foreach (var x in this.configuration.CrystalConfigurations)
        {
            ICrystal? crystal;
            if (x.Value is BigCrystalConfiguration)
            {// new BigCrystalImpl<TData>
                crystal = (IBigCrystal)Activator.CreateInstance(typeof(BigCrystalImpl<>).MakeGenericType(x.Key), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { this, }, null)!;
            }
            else
            {// new CrystalImpl<TData>
                crystal = (ICrystal)Activator.CreateInstance(typeof(CrystalImpl<>).MakeGenericType(x.Key), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { this, }, null)!;
            }

            crystal.Configure(x.Value);

            this.typeToCrystal.TryAdd(x.Key, crystal);
            this.crystals.TryAdd(crystal, 0);
        }

        /*foreach (var x in this.configuration.BigCrystalConfigurations)
        {
            // (IBigCrystal) new CrystalDataImpl<TData>
            var bigCrystal = (IBigCrystal)Activator.CreateInstance(typeof(BigCrystalImpl<>).MakeGenericType(x.Key), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { this, }, null)!;

            this.typeToBigCrystal.TryAdd(x.Key, bigCrystal);
        }

        foreach (var x in this.configuration.CrystalConfigurations)
        {
            ICrystal? crystal;
            if (!this.typeToBigCrystal.TryGetValue(x.Key, out var bigCrystal))
            {// Crystal
                // (ICrystal) new CrystalImpl<TData>
                crystal = (ICrystal)Activator.CreateInstance(typeof(CrystalImpl<>).MakeGenericType(x.Key), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { this, }, null)!;
            }
            else
            {// BigCrystal
                crystal = (ICrystal)bigCrystal;
            }

            crystal.Configure(x.Value);

            this.typeToCrystal.TryAdd(x.Key, crystal);
            this.crystals.TryAdd(crystal, 0);
        }*/
    }

    #region FieldAndProperty

    public bool EnableLogger { get; }

    public string RootDirectory { get; }

    public TimeSpan DefaultTimeout { get; }

    public IJournal? Journal => this.JournalInternal;

    public IStorageKey StorageKey { get; }

    internal UnitLogger UnitLogger { get; }

#pragma warning disable SA1401 // Fields should be private
    internal IJournalInternal? JournalInternal;
#pragma warning restore SA1401 // Fields should be private

    private CrystalizerConfiguration configuration;
    private ILogger logger;
    private ThreadsafeTypeKeyHashTable<ICrystal> typeToCrystal = new();
    private ConcurrentDictionary<ICrystal, int> crystals = new();
    // private ThreadsafeTypeKeyHashTable<IBigCrystal> typeToBigCrystal = new();
    // private ConcurrentDictionary<IBigCrystal, int> bigCrystals = new();

    private object syncObject = new();
    private IRawFiler? localFiler;
    private Dictionary<string, IRawFiler> bucketToS3Filer = new();

    #endregion

    #region Resolvers

    public IFiler ResolveFiler(PathConfiguration configuration)
        => new RawFilerToFiler(this, this.ResolveRawFiler(configuration), configuration);

    public async Task<IFiler> ResolveAndPrepareFiler(PathConfiguration configuration)
    {
        var filer = (IFiler)new RawFilerToFiler(this, this.ResolveRawFiler(configuration), configuration);
        await filer.PrepareAndCheck(this, configuration);
        return filer;
    }

    public IRawFiler ResolveRawFiler(PathConfiguration configuration)
    {
        lock (this.syncObject)
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
        lock (this.syncObject)
        {
            IStorage storage;
            if (configuration is EmptyStorageConfiguration emptyStorageConfiguration)
            {// Empty storage
                storage = EmptyStorage.Default;
            }
            else if (configuration is SimpleStorageConfiguration simpleStorageConfiguration)
            {
                storage = new SimpleStorage();
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

    public async Task<CrystalStartResult> PrepareJournal()
    {
        if (this.JournalInternal == null)
        {// New journal
            var configuration = this.configuration.JournalConfiguration;
            if (configuration is EmptyJournalConfiguration)
            {
                return CrystalStartResult.Success;
            }
            else if (configuration is SimpleJournalConfiguration simpleJournalConfiguration)
            {
                var simpleJournal = new SimpleJournal(this, simpleJournalConfiguration);
                this.JournalInternal = simpleJournal;
            }
            else
            {
                return CrystalStartResult.NoJournal;
            }
        }

        if (this.JournalInternal.Prepared)
        {
            return CrystalStartResult.Success;
        }
        else
        {// Prepare
            return await this.JournalInternal.Prepare(this).ConfigureAwait(false);
        }
    }

    /*public bool TryGetJournalWriter(JournalRecordType recordType, out JournalRecord record)
    {
        if (this.Journal == null)
        {
            record = default;
            return false;
        }

        this.Journal.GetJournalWriter(recordType, out record);
        return true;
    }*/

    public async Task<CrystalStartResult> PrepareAndLoadAll(CrystalStartParam? param = null)
    {
        param ??= CrystalStartParam.Default;

        var crystals = this.crystals.Keys.ToArray();
        foreach (var x in crystals)
        {
            var result = await x.PrepareAndLoad(param).ConfigureAwait(false);
            if (result != CrystalStartResult.Success)
            {
                return result;
            }
        }

        return CrystalStartResult.Success;
    }

    public async Task SaveAll(bool unload = false)
    {
        var crystals = this.crystals.Keys.ToArray();
        foreach (var x in crystals)
        {
            await x.Save(unload).ConfigureAwait(false);
        }
    }

    public async Task SaveAllAndTerminate()
    {
        var crystals = this.crystals.Keys.ToArray();
        foreach (var x in crystals)
        {
            await x.Save(true).ConfigureAwait(false);
        }

        // Terminate filers
        var tasks = new List<Task>();
        lock (this.syncObject)
        {
            if (this.localFiler is not null)
            {
                tasks.Add(this.localFiler.Terminate());
                this.localFiler = null;
            }

            foreach (var x in this.bucketToS3Filer.Values)
            {
                tasks.Add(x.Terminate());
            }

            this.bucketToS3Filer.Clear();
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public void DeleteAll()
    {
        var crystals = this.crystals.Keys.ToArray();
        foreach (var x in crystals)
        {
            x.Delete();
        }
    }

    public ICrystal<TData> CreateCrystal<TData>()
        where TData : ITinyhandSerialize<TData>, ITinyhandReconstruct<TData>
    {
        if (!this.typeToCrystal.TryGetValue(typeof(TData), out _))
        {
            ThrowTypeNotRegistered(typeof(TData));
        }

        var crystal = new CrystalImpl<TData>(this);
        this.crystals.TryAdd(crystal, 0);
        return crystal;
    }

    public ICrystal<TData> CreateBigCrystal<TData>()
        where TData : BaseData, ITinyhandSerialize<TData>, ITinyhandReconstruct<TData>
    {
        if (!this.typeToCrystal.TryGetValue(typeof(TData), out var c) ||
            c is not IBigCrystal)
        {
            ThrowTypeNotRegistered(typeof(TData));
        }

        var crystal = new BigCrystalImpl<TData>(this);
        this.crystals.TryAdd(crystal, 0);
        return crystal;
    }

    public ICrystal<TData> GetCrystal<TData>()
        where TData : ITinyhandSerialize<TData>, ITinyhandReconstruct<TData>
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

    public ICrystal[] GetArray()
    {
        return this.crystals.Keys.ToArray();
    }

    #endregion

    #region Misc

    public string GetRootedFile(string file)
        => PathHelper.GetRootedFile(this.RootDirectory, file);

    public static string GetRootedFile(Crystalizer? crystalizer, string file)
        => crystalizer == null ? file : PathHelper.GetRootedFile(crystalizer.RootDirectory, file);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ThrowIfNotRegistered<TData>()
    {
        if (!this.typeToCrystal.TryGetValue(typeof(TData), out _))
        {
            ThrowTypeNotRegistered(typeof(TData));
        }
    }

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

    internal bool DeleteInternal(ICrystal crystal)
    {
        return this.crystals.TryRemove(crystal, out _);
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

    /*[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal IBigCrystal GetBigCrystal(Type type)
    {
        if (!this.typeToBigCrystal.TryGetValue(type, out var crystal))
        {
            ThrowTypeNotRegistered(type);
        }

        return crystal!;
    }*/

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal object GetObject(Type type)
    {
        if (!this.typeToCrystal.TryGetValue(type, out var crystal))
        {
            ThrowTypeNotRegistered(type);
        }

        return crystal!.Object;
    }

    internal CrystalConfiguration GetCrystalConfiguration(Type type)
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
    }

    #endregion
}
