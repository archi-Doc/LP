// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CrystalData.Journal;

#pragma warning disable SA1401

namespace CrystalData;

// Data + Journal/Waypoint + Filer/FileConfiguration + Storage/StorageConfiguration
public class CrystalObject<TData> : ICrystal<TData>
    where TData : IJournalObject, ITinyhandSerialize<TData>, ITinyhandReconstruct<TData>
{
    public CrystalObject(Crystalizer crystalizer)
    {
        this.Crystalizer = crystalizer;
        this.CrystalConfiguration = CrystalConfiguration.Default;
    }

    #region FieldAndProperty

    protected SemaphoreLock semaphore = new();
    protected TData? obj;
    protected IFiler? filer;
    protected IStorage? storage;
    protected Waypoint waypoint;

    #endregion

    #region ICrystal

    public Crystalizer Crystalizer { get; }

    public CrystalConfiguration CrystalConfiguration { get; protected set; }

    object ICrystal.Object => ((ICrystal<TData>)this).Object!;

    public TData Object
    {
        get
        {
            if (this.obj is { } v)
            {
                return v;
            }

            using (this.semaphore.Lock())
            {
                // Prepare and load
                if (!this.Prepared)
                {
                    this.PrepareAndLoadInternal(null).Wait();
                }

                if (this.obj != null)
                {
                    return this.obj;
                }

                // Finally, reconstruct
                this.ReconstructObject();
                return this.obj;
            }
        }
    }

    public bool Prepared { get; protected set; }

    public IFiler Filer
    {
        get
        {
            if (this.filer is { } v)
            {
                return v;
            }

            using (this.semaphore.Lock())
            {
                if (this.filer != null)
                {
                    return this.filer;
                }

                this.ResolveAndPrepareFiler();
                return this.filer;
            }
        }
    }

    public IStorage Storage
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
            this.CrystalConfiguration = configuration;
            this.filer = null;
            this.storage = null;
            this.Prepared = false;
        }
    }

    void ICrystal.ConfigureFile(FileConfiguration configuration)
    {
        using (this.semaphore.Lock())
        {
            this.CrystalConfiguration = this.CrystalConfiguration with { FileConfiguration = configuration, };
            this.filer = null;
            this.Prepared = false;
        }
    }

    void ICrystal.ConfigureStorage(StorageConfiguration configuration)
    {
        using (this.semaphore.Lock())
        {
            this.CrystalConfiguration = this.CrystalConfiguration with { StorageConfiguration = configuration, };
            this.storage = null;
            this.Prepared = false;
        }
    }

    async Task<CrystalSourceAndResult> ICrystal.PrepareAndLoad(CrystalPrepareParam? param)
    {
        using (this.semaphore.Lock())
        {
            if (this.Prepared)
            {// Prepared
                return CrystalSourceAndResult.Success;
            }

            return await this.PrepareAndLoadInternal(param).ConfigureAwait(false);
        }
    }

    async Task<CrystalResult> ICrystal.Save(bool unload)
    {
        ulong hash;
        CrystalResult result;
        using (this.semaphore.Lock())
        {
            if (!this.Prepared || this.obj == null)
            {
                return CrystalResult.NotPrepared;
                /*if (await this.PrepareAndLoadInternal(null).ConfigureAwait(false) != CrystalStartResult.Success)
                {
                    return CrystalResult.NoData;
                }*/
            }

            if (this.Crystalizer.Journal is { } journal)
            {
                var newToken = journal.UpdateToken(this.waypoint.JournalToken, this.obj);
                this.waypoint = new(this.waypoint.JournalPosition, newToken, this.waypoint.Hash);
            }

            // var options = TinyhandSerializerOptions.Standard with { Token = this.waypoint.JournalToken, };
            var byteArray = TinyhandSerializer.SerializeObject(this.obj);
            hash = FarmHash.Hash64(byteArray.AsSpan());

            if (this.waypoint.Hash != hash)
            {// Save
                result = await this.filer!.WriteAsync(0, new(byteArray)).ConfigureAwait(false);
                if (result != CrystalResult.Success)
                {// Write error
                    return result;
                }
            }

            ulong journalPosition;
            if (this.Crystalizer.Journal != null)
            {
                journalPosition = AddJournal();
            }
            else
            {
                journalPosition = 0;
            }

            this.waypoint = new(journalPosition, this.waypoint.JournalToken, hash);

            var waypointFiler = this.filer!.CloneWithExtension(Waypoint.Extension);
            result = await waypointFiler.WriteAsync(0, new(this.waypoint.ToByteArray())).ConfigureAwait(false);
            return result;
        }

        ulong AddJournal()
        {
            this.Crystalizer.Journal.GetWriter(JournalRecordType.Waypoint, this.waypoint.JournalToken, out var writer);
            writer.Write(this.waypoint.JournalToken);
            writer.Write(hash);
            return this.Crystalizer.Journal.Add(writer);
        }
    }

    async Task<CrystalResult> ICrystal.Delete()
    {
        using (this.semaphore.Lock())
        {
            if (!this.Prepared)
            {
                await this.PrepareAndLoadInternal(null).ConfigureAwait(false);
            }

            // Delete file/storage
            if (this.filer?.DeleteAsync() is { } task)
            {
                await task.ConfigureAwait(false);
            }

            if (this.storage?.DeleteAllAsync() is { } task2)
            {
                await task2.ConfigureAwait(false);
            }

            // Journal/Waypoint
            this.Crystalizer.Journal?.UnregisterToken(this.waypoint.JournalToken);
            this.waypoint = default;

            // Clear
            this.CrystalConfiguration = CrystalConfiguration.Default;
            TinyhandSerializer.ReconstructObject<TData>(ref this.obj);
            this.filer = null;
            this.storage = null;

            this.Prepared = false;
            return CrystalResult.Success;
        }
    }

    void ICrystal.Terminate()
    {
    }

    #endregion

    protected virtual async Task<CrystalSourceAndResult> PrepareAndLoadInternal(CrystalPrepareParam? param)
    {// this.semaphore.Lock()
        if (this.Prepared)
        {
            return CrystalSourceAndResult.Success;
        }

        param ??= CrystalPrepareParam.Default;

        // Filer
        if (this.filer == null)
        {
            this.filer = this.Crystalizer.ResolveFiler(this.CrystalConfiguration.FileConfiguration);
            var result = await this.filer.PrepareAndCheck(this.Crystalizer, this.CrystalConfiguration.FileConfiguration).ConfigureAwait(false);
            if (result != CrystalResult.Success)
            {// Filer error
                var sourceAndResult = result.FromSource(CrystalSource.File);
                if (await param.Query(sourceAndResult).ConfigureAwait(false) == AbortOrContinue.Abort)
                {
                    return sourceAndResult;
                }
            }
        }

        // Storage
        if (this.storage == null)
        {
            this.storage = this.Crystalizer.ResolveStorage(this.CrystalConfiguration.StorageConfiguration);
            var result = await this.storage.PrepareAndCheck(this.Crystalizer, this.CrystalConfiguration.StorageConfiguration, false).ConfigureAwait(false);
            if (result != CrystalResult.Success)
            {
                var sourceAndResult = result.FromSource(CrystalSource.Storage);
                if (await param.Query(sourceAndResult).ConfigureAwait(false) == AbortOrContinue.Abort)
                {
                    return sourceAndResult;
                }
            }
        }

        // Load waypoint
        if (!this.waypoint.IsValid)
        {
            var waypointFiler = this.filer.CloneWithExtension(Waypoint.Extension);
            var waypointResult = await waypointFiler.ReadAsync(0, -1).ConfigureAwait(false);
            if (waypointResult.IsFailure ||
                !Waypoint.TryParse(waypointResult.Data.Memory.Span, out var waypoint))
            {// No waypoint file
                if (await param.Query(CrystalStartResult.FileNotFound).ConfigureAwait(false) == AbortOrComplete.Complete)
                {
                    return DataLost();
                }
                else
                {
                    return CrystalStartResult.DirectoryError;
                }
            }

            this.waypoint = waypoint;
        }

        // Load data
        var memoryResult = await this.filer.ReadAsync(0, -1).ConfigureAwait(false);
        if (memoryResult.IsFailure || FarmHash.Hash64(memoryResult.Data.Memory.Span) != this.waypoint.Hash)
        { // Data read error or Hash does not match
            if (await param.Query(CrystalStartResult.FileNotFound).ConfigureAwait(false) == AbortOrComplete.Complete)
            {
                return DataLost();
            }
            else
            {
                return CrystalStartResult.FileNotFound;
            }
        }
        else
        {// Deserialize
            try
            {
                TinyhandSerializer.DeserializeObject(memoryResult.Data.Memory.Span, ref this.obj);
                if (this.obj == null)
                {
                    return DataLost();
                }
            }
            catch
            {
                if (await param.Query(CrystalStartResult.FileNotFound).ConfigureAwait(false) == AbortOrContinue.Continue)
                {
                    return DataLost();
                }
                else
                {
                    return CrystalStartResult.DeserializeError;
                }
            }
            finally
            {
                memoryResult.Return();
            }
        }

        this.Crystalizer.Journal?.RegisterToken(this.waypoint.JournalToken, this.obj);

        this.Prepared = true;
        return CrystalSourceAndResult.Success;

        CrystalSourceAndResult DataLost()
        {
            TinyhandSerializer.ReconstructObject<TData>(ref this.obj);
            var hash = FarmHash.Hash64(TinyhandSerializer.SerializeObject(this.obj));

            ulong journalPosition = 1;
            uint journalToken = 0;
            if (this.Crystalizer.Journal is { } journal)
            {
                journalToken = journal.NewToken(this.obj);
                journal.GetWriter(JournalRecordType.Waypoint, journalToken, out var writer);
                journalPosition = journal.Add(writer);
            }

            this.waypoint = new(journalPosition, journalToken, hash);

            this.Prepared = true;
            return CrystalSourceAndResult.Success;
        }
    }

    [MemberNotNull(nameof(obj))]
    protected virtual void ReconstructObject()
    {
        TinyhandSerializer.ReconstructObject<TData>(ref this.obj);
    }

    [MemberNotNull(nameof(filer))]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void ResolveAndPrepareFiler()
    {
        if (this.filer == null)
        {
            this.filer = this.Crystalizer.ResolveFiler(this.CrystalConfiguration.FileConfiguration);
            this.filer.PrepareAndCheck(this.Crystalizer, this.CrystalConfiguration.FileConfiguration).Wait();
        }
    }

    [MemberNotNull(nameof(storage))]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void ResolveAndPrepareStorage()
    {
        if (this.storage == null)
        {
            this.storage = this.Crystalizer.ResolveStorage(this.CrystalConfiguration.StorageConfiguration);
            this.storage.PrepareAndCheck(this.Crystalizer, this.CrystalConfiguration.StorageConfiguration, false).Wait();
        }
    }
}
