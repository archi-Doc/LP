﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace CrystalData;

internal partial class MemoryObject
{
    public MemoryObject()
    {
    }

    internal (bool Changed, int NewSize) SetSpanInternal(ReadOnlySpan<byte> span)
    {
        if (this.memoryOwnerIsValid && span.SequenceEqual(this.memoryOwner.Memory.Span))
        {// Identical
            return (false, span.Length);
        }

        this.memoryOwner = this.memoryOwner.Return();

        this.@object = null;
        var owner = ByteArrayPool.Default.Rent(span.Length);
        this.memoryOwner = owner.ToReadOnlyMemoryOwner(0, span.Length);
        this.memoryOwnerIsValid = true;
        span.CopyTo(owner.ByteArray.AsSpan());
        return (true, this.memoryOwner.Memory.Length);
    }

    internal (bool Changed, int NewSize) SetMemoryOwnerInternal(ByteArrayPool.ReadOnlyMemoryOwner dataToBeMoved, object? obj)
    {
        if (this.memoryOwnerIsValid && dataToBeMoved.Memory.Span.SequenceEqual(this.memoryOwner.Memory.Span))
        {// Identical
            return (false, dataToBeMoved.Memory.Span.Length);
        }

        this.memoryOwner = this.memoryOwner.Return();

        this.@object = obj;
        this.memoryOwner = dataToBeMoved;
        this.memoryOwnerIsValid = true;
        return (true, this.memoryOwner.Memory.Length);
    }

    internal CrystalResult TryGetObjectInternal<T>(out T? obj)
        where T : ITinyhandSerialize<T>
    {
        if (this.@object is T t)
        {
            obj = t;
            return CrystalResult.Success;
        }

        try
        {
            obj = TinyhandSerializer.DeserializeObject<T>(this.memoryOwner.Memory.Span);
            if (obj != null)
            {
                this.@object = obj;
                return CrystalResult.Success;
            }
        }
        catch
        {
        }

        obj = default;
        return CrystalResult.DeserializeError;
    }

    internal void Clear()
    {
        this.@object = null;
        this.memoryOwnerIsValid = false;
        this.memoryOwner = this.memoryOwner.Return();
    }

    internal ReadOnlySpan<byte> Span => this.memoryOwner.Memory.Span;

    internal bool MemoryOwnerIsValid => this.memoryOwnerIsValid;

    internal ByteArrayPool.ReadOnlyMemoryOwner MemoryOwner => this.memoryOwner;

    private object? @object;
    private bool memoryOwnerIsValid = false;
    private ByteArrayPool.ReadOnlyMemoryOwner memoryOwner;
}
