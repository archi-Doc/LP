﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Netsphere;

[TinyhandObject]
public sealed partial class TrustSource<T>
    where T : IEquatable<T>, ITinyhandSerialize<T>
{
    public enum TrustState
    {
        Unfixed,
        Fixed,
    }

    public TrustSource(int capacity, int trustMinimum)
    {
        Debug.Assert(capacity > 0);
        Debug.Assert(trustMinimum > 0);
        Debug.Assert(capacity >= trustMinimum);

        this.Capacity = capacity;
        this.TrustMinimum = trustMinimum;
        // this.itemPool = new(() => new(), this.Capacity);
        this.counterPool = new(() => new(), this.Capacity);
    }

    internal TrustSource()
    {
        // this.itemPool = default!;
        this.counterPool = default!;
    }

    [TinyhandObject]
    [ValueLinkObject]
    private partial class Item
    {
        [Link(Primary = true, Type = ChainType.QueueList, Name = "Queue")]
        public Item()
        {
        }

        [Key(0)]
        public Counter Counter { get; set; } = default!;

        // [Key(1)]
        // public long AddedMics { get; set; }
    }

    [TinyhandObject]
    [ValueLinkObject]
    private partial class Counter
    {
        public Counter()
        {
        }

        public Counter(T value)
        {
            this.Value = value;
        }

        [Key(0)]
        [Link(Primary = true, Type = ChainType.Unordered, AddValue = false)]
        public T? Value { get; set; }

        [Key(1)]
        [Link(Type = ChainType.Ordered)]
        public long Count { get; set; }
    }

    #region FieldAndProperty

    public int Capacity { get; private set; }

    public int TrustMinimum { get; private set; }

    public bool IsFixed => this.isFixed;

    private readonly object syncObject = new();
    // private readonly ObjectPool<Item> itemPool;
    private readonly ObjectPool<Counter> counterPool;

    [Key(0)]
    private Item.GoshujinClass items = new(); // lock (this.syncObject)

    [Key(1)]
    private Counter.GoshujinClass counters = new(); // lock (this.syncObject)

    private bool isFixed;
    private T? fixedValue;

    #endregion

    public void Add(T value)
    {
        lock (this.syncObject)
        {
            Counter? counter;
            if (this.counters.ValueChain.TryGetValue(value, out counter))
            {// Increment counter
                counter.CountValue++;
            }
            else
            {// New counter
                counter = this.counterPool.Get();
                counter.Value = value;
                counter.Count = 1;
                counter.Goshujin = this.counters;
            }

            Item item;
            if (this.items.Count >= this.Capacity)
            {
                item = this.items.QueueChain.Dequeue();

                counter = item.Counter;
                counter.CountValue--;
                if (counter.CountValue == 0)
                {// Return counter
                    counter.Goshujin = null;
                    this.counterPool.Return(counter);
                }
            }
            else
            {
                item = new();
            }

            item.Counter = counter;
            this.items.QueueChain.Enqueue(item);

            if (this.isFixed)
            {// Fixed
                if (this.fixedValue!.Equals(value))
                {// Identical
                    return;
                }

                var last = this.counters.CountChain.Last;
                if (last is not null && this.CanFix(last))
                {// Fix -> Unfix
                    this.ClearInternal();
                }
            }
            else
            {// Not fixed
                var last = this.counters.CountChain.Last;
                if (last is not null && this.CanFix(last))
                {// Fix
                    this.isFixed = true;
                    this.fixedValue = last.Value;
                }
            }
        }
    }

    public bool TryGet([MaybeNullWhen(false)] out T value)
    {
        value = this.fixedValue;
        return this.isFixed && value is not null;
    }

    public void Clear()
    {
        lock (this.syncObject)
        {
            this.ClearInternal();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ClearInternal()
    {
        this.items.Clear();
        this.counters.Clear();
        this.isFixed = false;
        this.fixedValue = default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanFix(Counter counter)
    {
        return counter.Count >= this.TrustMinimum &&
            counter.Count >= (this.items.Count >> 1);
    }

    /*void ITinyhandSerializationCallback.OnAfterReconstruct()
    {
    }

    void ITinyhandSerializationCallback.OnAfterDeserialize()
    {
    }

    void ITinyhandSerializationCallback.OnBeforeSerialize()
    {
    }*/
}
