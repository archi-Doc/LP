﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace ZenItz;

[TinyhandObject(ExplicitKeyOnly = true)]
[ValueLinkObject]
internal partial class SecondaryObject
{
    public enum SecondaryState
    {
        NotLoaded, // Not active and saved
        Loaded, // Active and not saved
        Saved, // Active and saved
    }

    internal SecondaryObject()
    {// For serializer
    }

    internal SecondaryObject(Identifier secondaryId)
    {// New object, lock (secondaryGoshujin)
        this.State = SecondaryState.Saved;
        this.secondaryId = secondaryId;
        this.FlakeId = FlakeControl.Instance.GetFlakeId();
    }

    internal void Set(PrimaryObject primaryObject, ReadOnlySpan<byte> data)
    {// lock (secondaryGoshujin)
        if (this.himo != null && data.SequenceEqual(this.himo.MemoryOwner.Memory.Span))
        {// Identical
            return;
        }

        this.State = SecondaryState.Loaded;
        this.himo = primaryObject.Zen.HimoControl.Create(in primaryObject.primaryId, in this.secondaryId, data);
    }

    public SecondaryState State { get; private set; }

    [Key(0)]
    [Link(Primary = true, Type = ChainType.Unordered)]
    private Identifier secondaryId;

    public Identifier SecondaryId => this.secondaryId;

    [Key(1)]
    public int FlakeId { get; private set; }

    [Key(2)]
    public long Position { get; private set; } = -1; // -1: Not saved

    [Key(3)]
    public int Size { get; private set; }

    private Himo? himo;
}
