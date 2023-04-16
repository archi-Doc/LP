// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace CrystalData;

public class BigCrystalImpl<TData> : CrystalObject<TData>, IBigCrystal<TData>, ICrystal
    where TData : BaseData, IJournalObject, ITinyhandSerialize<TData>, ITinyhandReconstruct<TData>
{
    public BigCrystalImpl(Crystalizer crystalizer)
        : base(crystalizer)
    {
        this.BigCrystalConfiguration = BigCrystalConfiguration.Default; // crystalizer.GetBigCrystalConfiguration(typeof(TData));
        this.storageGroup = new(crystalizer);
        this.himoGoshujin = new(this);
        this.logger = crystalizer.UnitLogger.GetLogger<IBigCrystal<TData>>();
        this.storageFileConfiguration = EmptyFileConfiguration.Default;
        this.crystalFileConfiguration = EmptyFileConfiguration.Default;
    }

    #region FieldAndProperty

    public BigCrystalConfiguration BigCrystalConfiguration { get; protected set; }

    public DatumRegistry DatumRegistry { get; } = new();

    public StorageGroup StorageGroup => this.storageGroup;

    public HimoGoshujinClass Himo => this.himoGoshujin;

    public long MemoryUsage => this.himoGoshujin.MemoryUsage;

    private StorageGroup storageGroup;
    private HimoGoshujinClass himoGoshujin;
    private ILogger logger;
    private PathConfiguration storageFileConfiguration;
    private IFiler? storageGroupFiler;
    private PathConfiguration crystalFileConfiguration;
    private IFiler? crystalFiler;

    #endregion

    #region ICrystal

    void IBigCrystal.Configure(BigCrystalConfiguration configuration)
    {
        using (this.semaphore.Lock())
        {
            this.BigCrystalConfiguration = configuration;
            this.CrystalConfiguration = configuration;

            this.BigCrystalConfiguration.RegisterDatum(this.DatumRegistry);
            this.storageFileConfiguration = this.BigCrystalConfiguration.DirectoryConfiguration.CombinePath(this.BigCrystalConfiguration.StorageFile);
            this.crystalFileConfiguration = this.BigCrystalConfiguration.DirectoryConfiguration.CombinePath(this.BigCrystalConfiguration.CrystalFile);

            this.filer = null;
            this.storage = null;
            this.Prepared = false;
        }
    }

    async Task<CrystalResult> ICrystal.Save(bool unload)
    {
        using (this.semaphore.Lock())
        {
            if (!this.Prepared)
            {
                var result = await this.PrepareAndLoadInternal(null).ConfigureAwait(false);
                if (result.IsFailure)
                {
                    return result.Result;
                }
            }

            // Save & Unload datum and metadata.
            this.obj?.Save(unload);

            // Save storages
            await this.StorageGroup.SaveStorage().ConfigureAwait(false);

            // Save crystal
            await PathHelper.SaveData(this.Crystalizer, this.obj, this.crystalFiler, 0).ConfigureAwait(false);

            // Save storage group
            if (this.storageGroupFiler != null)
            {
                await this.StorageGroup.SaveGroup(this.storageGroupFiler).ConfigureAwait(false);
            }

            this.logger.TryGet()?.Log($"Crystal stop - {this.himoGoshujin.MemoryUsage}");
        }

        return CrystalResult.Success;
    }

    public async Task Abort()
    {
        using (this.semaphore.Lock())
        {
            if (!this.Prepared)
            {
                await this.PrepareAndLoadInternal(null).ConfigureAwait(false);
            }

            await this.StorageGroup.SaveStorage().ConfigureAwait(false);
            this.StorageGroup.Clear();
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

            await this.DeleteAllInternal().ConfigureAwait(false);

            // Clear
            this.CrystalConfiguration = CrystalConfiguration.Default;
            this.filer = null;

            return CrystalResult.Success;
        }
    }

    internal async Task DeleteAllInternal()
    {
        this.obj?.Delete();
        this.himoGoshujin.Clear();

        this.crystalFiler?.DeleteAndForget();
        this.crystalFiler = null;

        this.storageGroupFiler?.DeleteAndForget();
        this.storageGroupFiler = null;

        await this.StorageGroup.DeleteAllAsync();

        this.ReconstructObject();
    }

    #endregion

    protected override async Task<CrystalSourceAndResult> PrepareAndLoadInternal(CrystalPrepareParam? param)
    {// this.semaphore.Lock()
        param ??= CrystalPrepareParam.Default;
        if (param.FromScratch)
        {
            await this.StorageGroup.PrepareAndCheck(this.CrystalConfiguration.StorageConfiguration, param, null).ConfigureAwait(false);

            await this.DeleteAllInternal();
            this.ReconstructObject();

            this.Prepared = true;
            return CrystalSourceAndResult.Success;
        }

        if (this.Prepared)
        {
            return CrystalSourceAndResult.Success;
        }

        // Storage group filer
        if (this.storageGroupFiler == null)
        {
            this.storageGroupFiler = this.Crystalizer.ResolveFiler(this.storageFileConfiguration);
            var storageGroupResult = await this.storageGroupFiler.PrepareAndCheck(this.Crystalizer, this.storageFileConfiguration).ConfigureAwait(false);
            if (storageGroupResult != CrystalResult.Success)
            {
                var sourceAndResult = new CrystalSourceAndResult(CrystalSource.StorageGroup, storageGroupResult);
                if (await param.Query(sourceAndResult).ConfigureAwait(false) == AbortOrContinue.Abort)
                {
                    return sourceAndResult;
                }
            }
        }

        // Load storage group
        var result = await this.LoadStorageGroup(param).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return result;
        }

        var info = this.StorageGroup.GetInformation();
        foreach (var x in info)
        {
            this.logger.TryGet()?.Log(x);
        }

        // Crystal filer
        if (this.crystalFiler == null)
        {
            this.crystalFiler = this.Crystalizer.ResolveFiler(this.crystalFileConfiguration);
            var crystalResult = await this.crystalFiler.PrepareAndCheck(this.Crystalizer, this.crystalFileConfiguration).ConfigureAwait(false);
            if (crystalResult != CrystalResult.Success)
            {
                var sourceAndResult = new CrystalSourceAndResult(CrystalSource.File, crystalResult);
                if (await param.Query(sourceAndResult).ConfigureAwait(false) == AbortOrContinue.Abort)
                {
                    return sourceAndResult;
                }
            }
        }

        // Load Crystal
        result = await this.LoadCrystal(param).ConfigureAwait(false);
        if (result != CrystalStartResult.Success)
        {
            return result;
        }

        this.Prepared = true;
        return result;
    }

    protected override void ReconstructObject()
    {
        this.obj = TinyhandSerializer.Reconstruct<TData>();
        this.obj.Initialize(this, null, true);
    }

    private async Task<CrystalSourceAndResult> LoadStorageGroup(CrystalPrepareParam param)
    {// await this.semaphore.WaitAsync().ConfigureAwait(false)
        CrystalSourceAndResult result;

        var (dataResult, _) = await PathHelper.LoadData(this.storageGroupFiler).ConfigureAwait(false);
        if (dataResult.IsFailure)
        {
            if (await param.Query(new(CrystalSource.StorageGroup, dataResult.Result)).ConfigureAwait(false) == AbortOrContinue.Continue)
            {
                result = await this.StorageGroup.PrepareAndCheck(this.CrystalConfiguration.StorageConfiguration, param, null).ConfigureAwait(false);
                if (result.IsSuccess || param.ForceStart)
                {
                    return CrystalSourceAndResult.Success;
                }

                return result;
            }
            else
            {
                return new CrystalSourceAndResult(CrystalSource.StorageGroup, dataResult.Result);
            }
        }

        result = await this.StorageGroup.PrepareAndCheck(this.CrystalConfiguration.StorageConfiguration, param, dataResult.Data.Memory).ConfigureAwait(false);
        if (result.IsSuccess || param.ForceStart)
        {
            return CrystalSourceAndResult.Success;
        }

        return result;
    }

    private async Task<CrystalStartResult> LoadCrystal(CrystalPrepareParam param)
    {// await this.semaphore.WaitAsync().ConfigureAwait(false)
        var (dataResult, _) = await PathHelper.LoadData(this.crystalFiler).ConfigureAwait(false);
        if (dataResult.IsFailure)
        {
            if (await param.Query(CrystalStartResult.FileNotFound).ConfigureAwait(false) == AbortOrContinue.Continue)
            {
                return CrystalStartResult.Success;
            }
            else
            {
                return CrystalStartResult.FileNotFound;
            }
        }

        if (!this.DeserializeCrystal(dataResult.Data.Memory))
        {
            if (await param.Query(CrystalStartResult.FileError).ConfigureAwait(false) == AbortOrContinue.Continue)
            {
                return CrystalStartResult.Success;
            }
            else
            {
                return CrystalStartResult.FileError;
            }
        }

        return CrystalStartResult.Success;
    }

    private bool DeserializeCrystal(ReadOnlyMemory<byte> data)
    {
        if (!TinyhandSerializer.TryDeserialize<TData>(data.Span, out var tdata))
        {
            return false;
        }

        tdata.Initialize(this, null, true);
        this.obj = tdata;

        this.himoGoshujin.Clear();

        return true;
    }
}
