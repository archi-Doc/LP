﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace LP;

/// <summary>
/// A thread-safe pool of fixed-length (1 kbytes or more) byte arrays (uses <see cref="ConcurrentQueue{T}"/>).<br/>
/// </summary>
public class ByteArrayPool
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnAndSetNull(ref Owner? owner)
    {
        if (owner != null)
        {
            owner = owner.Return();
        }
    }

    /// <summary>
    /// An owner class of a byte array (one owner for each byte array).<br/>
    /// <see cref="Owner"/> has a reference count, and when it reaches zero, it returns the byte array to the pool.
    /// </summary>
    public class Owner
    {
        public Owner(byte[] byteArray)
        {
            this.Pool = null;
            this.ByteArray = byteArray;
            this.SetCount1();
        }

        internal Owner(ByteArrayPool pool)
        {
            this.Pool = pool;
            this.ByteArray = new byte[pool.ArrayLength];
            this.SetCount1();
        }

        /// <summary>
        ///  Increment the reference count.
        /// </summary>
        /// <returns><see cref="Owner"/> instance (<see langword="this"/>).</returns>
        public Owner IncrementAndShare()
        {
            Interlocked.Increment(ref this.count);
            return this;
        }

        public MemoryOwner IncrementAndShare(int start, int length)
        {
            Interlocked.Increment(ref this.count);
            return new MemoryOwner(this, start, length);
        }

        /// <summary>
        /// Decrement the reference count. When it reaches zero, it returns the byte array to the pool.<br/>
        /// Failure to return a rented array is not a fatal error (eventually be garbage-collected).
        /// </summary>
        /// <returns><see langword="null"></see>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Owner? Return()
        {
            var count = Interlocked.Decrement(ref this.count);
            if (count == 0 && this.Pool != null)
            {
                // this.Pool.Return(this);
                if (this.Pool.MaxPool == 0 || this.Pool.queue.Count <= this.Pool.MaxPool)
                {
                    this.Pool.queue.Enqueue(this);
                }
            }

            return null;
        }

        internal void SetCount1() => Volatile.Write(ref this.count, 1);

        /// <summary>
        /// Gets a <see cref="ByteArrayPool"/> instance.
        /// </summary>
        public ByteArrayPool? Pool { get; }

        /// <summary>
        /// Gets a fixed-length byte array.
        /// </summary>
        public byte[] ByteArray { get; }

        /// <summary>
        /// Gets the reference count of the owner.
        /// </summary>
        public int Count => Volatile.Read(ref this.count);

        private int count;
    }

    public readonly struct MemoryOwner
    {
        public MemoryOwner(Owner owner)
        {
            this.Owner = owner;
            this.Memory = owner.ByteArray.AsMemory();
        }

        public MemoryOwner(Owner owner, int start, int length)
        {
            this.Owner = owner;
            this.Memory = owner.ByteArray.AsMemory(start, length);
        }

        public MemoryOwner(Owner owner, Memory<byte> memory)
        {
            this.Owner = owner;
            this.Memory = memory;
        }

        /// <summary>
        ///  Increment the reference count.
        /// </summary>
        /// <returns><see cref="Owner"/> instance (<see langword="this"/>).</returns>
        public MemoryOwner IncrementAndShare()
        {
            if (this.Owner == null)
            {
                throw new InvalidOperationException();
            }

            return new(this.Owner.IncrementAndShare(), this.Memory);
        }

        public MemoryOwner IncrementAndShare(int start, int length)
        {
            if (this.Owner == null)
            {
                throw new InvalidOperationException();
            }

            return new(this.Owner.IncrementAndShare(), start, length);
        }

        public MemoryOwner Return()
        {
            this.Owner?.Return();
            return default;
        }

        public readonly Owner? Owner;
        public readonly Memory<byte> Memory;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ByteArrayPool"/> class.<br/>
    /// </summary>
    /// <param name="arrayLength">The length of fixed-length byte array.</param>
    /// <param name="maxPool">The maximum number of pooled arrays (0 for unlimited).</param>
    public ByteArrayPool(int arrayLength, int maxPool = 0)
    {
        this.ArrayLength = arrayLength;
        this.MaxPool = maxPool >= 0 ? maxPool : 0;
    }

    /// <summary>
    /// Gets a fixed-length byte array from the pool or create a new byte array if not available.<br/>
    /// </summary>
    /// <returns>A fixed-length byte array.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Owner Rent()
    {
        Owner? owner;
        if (!this.queue.TryDequeue(out owner))
        {// Allocate a new byte array.
            return new Owner(this);
        }

        owner.SetCount1();
        return owner;
    }

    /// <summary>
    /// Gets a fixed-length byte array from the pool or create a new byte array if not available.<br/>
    /// </summary>
    /// <param name="start">The index of the first byte to include in the new <see cref="Memory{T}"/>.</param>
    /// <param name="length">The number of bytes to include in the new <see cref="Memory{T}"/>.</param>
    /// <returns>A fixed-length byte array.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MemoryOwner Rent(int start, int length)
    {
        Owner? owner;
        if (!this.queue.TryDequeue(out owner))
        {// Allocate a new byte array.
            owner = new Owner(this);
        }
        else
        {
            owner.SetCount1();
        }

        return new MemoryOwner(owner, start, length);
    }

    /*/// <summary>
    /// Returns a byte array to the pool.<br/>
    /// Failure to return a rented array is not a fatal error (eventually be garbage-collected).
    /// </summary>
    /// <param name="owner">An owner of the byte array to return to the pool.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Return(Owner owner)
    {
        if (owner.ByteArray.Length == this.ArrayLength)
        {
            if (this.MaxPool == 0 || this.queue.Count <= this.MaxPool)
            {
                this.queue.Enqueue(owner);
            }
        }
    }*/

    /// <summary>
    /// Gets the length of fixed-length byte array.
    /// </summary>
    public int ArrayLength { get; }

    /// <summary>
    /// Gets the maximum number of pooled arrays (0 for unlimited).
    /// </summary>
    public int MaxPool { get; }

    private ConcurrentQueue<Owner> queue = new();
}
