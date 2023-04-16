// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CrystalData.Journal;
using Tinyhand.IO;

#pragma warning disable SA1401

namespace CrystalData;

// Data + Journal/Waypoint + Filer/FileConfiguration.
// Not thread-safe
/*public partial class CrystalObject<TData> : IJournalObject
{
    public CrystalObject(Crystalizer crystalizer)
    {
        this.Crystalizer = crystalizer;
    }

    #region FieldAndProperty

    public Crystalizer Crystalizer { get; protected set; } = default!;

    public bool Prepared { get; protected set; }

    public Waypoint Waypoint { get; protected set; }

    public FileConfiguration FileConfiguration { get; protected set; } = EmptyFileConfiguration.Default;

    public TData Object
    {
        get
        {
            if (this.obj != null)
            {
                return this.obj;
            }

            // Load
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

    public IFiler Filer
    {
        get
        {
            if (this.filer != null)
            {
                return this.filer;
            }

            this.ResolveAndPrepareFiler();
            return this.filer;
        }
    }

    protected TData? obj;
    protected IFiler? filer;

    #endregion

    public void Configure(FileConfiguration configuration)
    {
        this.FileConfiguration = configuration;
        this.filer = null;
        this.Prepared = false;
    }

    public async Task<CrystalStartResult> PrepareAndLoad(CrystalStartParam? param)
    {
        if (this.Prepared)
        {// Prepared
            return CrystalStartResult.Success;
        }

        return await this.PrepareAndLoadInternal(param).ConfigureAwait(false);
    }

    public async Task<(CrystalMemoryOwnerResult Result, Waypoint Waypoint)> LoadData()
    {
        var result = await this.Filer.ReadAsync(0, -1).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return (new(result.Result), Waypoint.Invalid);
        }

        // Load check file (hash/location)
        var waypointFiler = this.Filer.CloneWithExtension(Waypoint.Extension);
        var waypointResult = await waypointFiler.ReadAsync(0, -1).ConfigureAwait(false);
        if (waypointResult.IsFailure ||
            !Waypoint.TryParse(waypointResult.Data.Memory.Span, out var waypoint))
        {// No waypoint file
            return (result, Waypoint.Invalid);
        }

        if (FarmHash.Hash64(result.Data.Memory.Span) != waypoint.Hash)
        {// Hash does not match
            return (new(CrystalResult.CorruptedData), waypoint);
        }

        return (result, waypoint);
    }

    public static Task<(CrystalResult Result, Waypoint Waypoiint)> SaveData<T>(Crystalizer crystalizer, T? obj, IFiler? filer, uint journalToken)
        where T : ITinyhandSerialize<T>
    {
        if (obj == null)
        {
            return Task.FromResult((CrystalResult.NoData, Waypoint.Invalid));
        }
        else if (filer == null)
        {
            return Task.FromResult((CrystalResult.NoFiler, Waypoint.Invalid));
        }

        // var option = TinyhandSerializer.DefaultOptions with { JournalToken = journalToken, };
        var data = TinyhandSerializer.SerializeObject(obj);
        return SaveData(crystalizer, data, filer, journalToken);
    }

    public static async Task<(CrystalResult Result, Waypoint Waypoiint)> SaveData(Crystalizer crystalizer, byte[]? data, IFiler? filer, uint journalToken)
    {
        if (data == null)
        {
            return (CrystalResult.NoData, Waypoint.Invalid);
        }
        else if (filer == null)
        {
            return (CrystalResult.NoFiler, Waypoint.Invalid);
        }

        var result = await filer.WriteAsync(0, new(data)).ConfigureAwait(false);
        if (result != CrystalResult.Success)
        {
            return (result, Waypoint.Invalid);
        }

        var hash = FarmHash.Hash64(data.AsSpan());

        ulong journalPosition;
        if (crystalizer.Journal != null)
        {
            journalPosition = AddJournal();
        }
        else
        {
            journalPosition = 0;
        }

        var waypoint = new Waypoint(journalPosition, journalToken, hash);
        var chckFiler = filer.CloneWithExtension(Waypoint.Extension);
        result = await chckFiler.WriteAsync(0, new(waypoint.ToByteArray())).ConfigureAwait(false);
        return (result, waypoint);

        ulong AddJournal()
        {
            crystalizer.Journal.GetWriter(JournalRecordType.Check, out var writer);
            writer.Write(journalToken);
            writer.Write(hash);
            journalPosition = crystalizer.Journal.Add(writer);

            return journalPosition;
        }
    }

    public virtual async Task<CrystalStartResult> PrepareAndLoadInternal(CrystalStartParam? param)
    {// this.semaphore.Lock()
        if (this.Prepared)
        {
            return CrystalStartResult.Success;
        }

        param ??= CrystalStartParam.Default;

        // Filer
        this.ResolveFiler();
        var result = await this.filer.PrepareAndCheck(this.Crystalizer, this.CrystalConfiguration.FileConfiguration).ConfigureAwait(false);
        if (result != CrystalResult.Success)
        {
            if (await param.Query(CrystalStartResult.FileNotFound).ConfigureAwait(false) == AbortOrContinue.Abort)
            {
                return CrystalStartResult.DirectoryError;
            }
        }

        // Load
        var memoryResult = await this.filer.ReadAsync(0, -1).ConfigureAwait(false);
        if (!memoryResult.IsSuccess)
        {
            if (await param.Query(CrystalStartResult.FileNotFound).ConfigureAwait(false) == AbortOrContinue.Continue)
            {
                ReconstructObject();
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
                TinyhandSerializer.DeserializeObject<TData>(memoryResult.Data.Memory.Span, ref this.obj);
                this.savedHash = FarmHash.Hash64(memoryResult.Data.Memory.Span);
            }
            catch
            {
                if (await param.Query(CrystalStartResult.FileNotFound).ConfigureAwait(false) == AbortOrComplete.Complete)
                {
                    ReconstructObject();
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

        // Storage
        this.ResolveStorage();
        result = await this.storage.PrepareAndCheck(this.Crystalizer, this.CrystalConfiguration.StorageConfiguration, false).ConfigureAwait(false);
        if (result != CrystalResult.Success)
        {
            return CrystalStartResult.DirectoryError;
        }

        this.Prepared = true;
        return CrystalStartResult.Success;

        void ReconstructObject()
        {
            TinyhandSerializer.ReconstructObject<TData>(ref this.obj);
            this.savedHash = FarmHash.Hash64(TinyhandSerializer.SerializeObject<TData>(this.obj));
        }
    }

    void IJournalObject.ReadJournal(ref TinyhandReader reader)
    {
    }

    [MemberNotNull(nameof(obj))]
    protected virtual void ReconstructObject()
    {
        this.obj = TinyhandSerializer.Reconstruct<TData>();
    }

    [MemberNotNull(nameof(filer))]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void ResolveFiler()
    {
        this.filer ??= this.Crystalizer.ResolveFiler(this.FileConfiguration);
    }

    [MemberNotNull(nameof(filer))]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void ResolveAndPrepareFiler()
    {
        if (this.filer == null)
        {
            this.filer = this.Crystalizer.ResolveFiler(this.FileConfiguration);
            this.filer.PrepareAndCheck(this.Crystalizer, this.FileConfiguration).Wait();
        }
    }
}*/
