﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace ZenItz;

#pragma warning disable SA1307 // Accessible fields should begin with upper-case letter
#pragma warning disable SA1401 // Fields should be private

[TinyhandObject(ExplicitKeyOnly = true)]
[ValueLinkObject]
public partial class Flake
{
    internal Flake()
    {
    }

    internal Flake(Zen zen, Identifier identifier)
    {
        this.Zen = zen;
        this.identifier = identifier;
    }

    public ZenResult Set(ReadOnlySpan<byte> data) => this.SetInternal(data, false);

    public async Task<ZenDataResult> Get()
    {
        SnowFlakeIdSegment idSegment = default;
        lock (this.syncObject)
        {
            if (this.IsRemoved)
            {
                return new(ZenResult.Removed);
            }

            if (this.snowFlakeObject != null)
            {// Loaded
                return new(ZenResult.Success, this.snowFlakeObject.MemoryOwner.IncrementAndShareReadOnly());
            }

            idSegment = new(this.flakeSnowId, this.flakeSnowSegment);
        }

        if (idSegment.IsValid)
        {
            return await this.Zen.SnowmanControl.TryLoadPrimary(idSegment, this.Identifier).ConfigureAwait(false);
        }

        return new(ZenResult.NoData);
    }

    public ZenResult Set(Identifier fragmentId, ReadOnlySpan<byte> data)
    {
        if (data.Length > Zen.MaxFragmentSize)
        {
            return ZenResult.OverSizeLimit;
        }

        lock (this.syncObject)
        {
            if (this.IsRemoved)
            {
                return ZenResult.Removed;
            }

            this.snowFragmentObject ??= new(this, this.Zen.SnowFragmentGoshujin);
            return this.snowFragmentObject.Set(fragmentId, data, false);
        }
    }

    public void Unload()
    {
    }

    public bool TryRemove() => this.Zen.TryRemove(this.Identifier);

    public Zen Zen { get; } = default!;

    public Identifier Identifier => this.identifier;

    public bool IsRemoved => this.Goshujin == null;

    internal ZenResult SetInternal(ReadOnlySpan<byte> data, bool loading)
    {
        if (data.Length > Zen.MaxFlakeSize)
        {
            return ZenResult.OverSizeLimit;
        }

        lock (this.syncObject)
        {
            if (this.IsRemoved)
            {
                return ZenResult.Removed;
            }

            if (!loading || this.snowFlakeObject == null)
            {// Not loading or Loading & empty
                this.snowFlakeObject ??= new(this, this.Zen.SnowFlakeGoshujin);
                this.snowFlakeObject.Set(data, loading);
            }
        }

        return ZenResult.Success;
    }

    internal ZenResult SetInternal(Identifier fragmentId, ReadOnlySpan<byte> data, bool loading)
    {
        if (data.Length > Zen.MaxFragmentSize)
        {
            return ZenResult.OverSizeLimit;
        }

        lock (this.syncObject)
        {
            if (this.IsRemoved)
            {
                return ZenResult.Removed;
            }

            if (!loading || this.snowFragmentObject == null)
            {// Not loading or Loading & empty
                this.snowFragmentObject ??= new(this, this.Zen.SnowFragmentGoshujin);
                return this.snowFragmentObject.Set(fragmentId, data, loading);
            }
            else
            {// Loading & not empty
                return this.snowFragmentObject.Set(fragmentId, data, loading);
            }
        }
    }

    internal void CreateInternal(Flake.GoshujinClass goshujin)
    {// lock (flakeGoshujin)
        lock (this.syncObject)
        {
            if (this.Goshujin == null)
            {
                this.Goshujin = goshujin;
            }
        }
    }

    internal bool RemoveInternal()
    {// lock (flakeGoshujin)
        lock (this.syncObject)
        {
            if (this.Goshujin == null)
            {
                return false;
            }
            else
            {
                this.Goshujin = null;
                return true;
            }
        }
    }

    [Key(0)]
    [Link(Primary = true, Name = "Id", NoValue = true, Type = ChainType.Unordered)]
    [Link(Name = "OrderedId", Type = ChainType.Ordered)]
    internal Identifier identifier;

    /// <summary>
    /// Gets Snowman id ((uint)(SnowFlakeId >> 32)) + Flake id ((uint)SnowFlakeId).<br/>
    /// 0: Unassigned.
    /// </summary>
    [Key(1)]
    internal ulong flakeSnowId;

    /// <summary>
    /// Gets a segment (offset: (int)(Segment >> 32), count: (int)(Segment)) of the flake.
    /// </summary>
    [Key(2)]
    internal long flakeSnowSegment;

    [Key(3)]
    internal ulong fragmentSnowId;

    [Key(4)]
    internal long fragmentSnowSegment;

    private object syncObject = new();
    private SnowFlakeObject? snowFlakeObject;
    private SnowFragmentObject? snowFragmentObject;
}
