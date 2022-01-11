// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace LP;

/// <summary>
/// A thread-safe pool of byte arrays (uses <see cref="ConcurrentQueue{T}"/>).<br/>
/// <see cref="ByteArrayPool"/> is slightly slower than 'new byte[]' or <see cref="System.Buffers.ArrayPool{T}"/> (especially byte arrays of 1kbytes or less), but it has some advantages.<br/>
/// 1. Can handle a rent byte array and a created ('new byte[]') byte array in the same way.<br/>
/// 2. By using <see cref="ByteArrayPool.MemoryOwner"/>, you can handle a rent byte array in the same way as <see cref="Memory{T}"/>.<br/>
/// 3. Can be used by multiple users by incrementing the reference count.
/// </summary>
public class ByteArrayPool
{
    private const int UpperBoundLength = 1024 * 1024 * 1024; // 1 GB
    private const int LowerBoundBits = 3;

    /// <summary>
    /// Represents an owner of a byte array (one owner instance for each byte array).<br/>
    /// <see cref="Owner"/> has a reference count, and when it reaches zero, it returns the byte array to the pool.
    /// </summary>
    public class Owner : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Owner"/> class from a byte array.<br/>
        /// This is a feature for compatibility with conventional memory management (e.g new byte[]), <br/>
        /// The byte array will not be returned when <see cref="Return"/> is called.
        /// </summary>
        /// <param name="byteArray">A byte array (allocated with 'new').</param>
        public Owner(byte[] byteArray)
        {
            this.bucket = null;
            this.ByteArray = byteArray;
            this.SetCount1();
        }

        internal Owner(Bucket bucket)
        {
            this.bucket = bucket;
            this.ByteArray = new byte[bucket.ArrayLength];
            this.SetCount1();
        }

        /// <summary>
        ///  Increment the reference count.
        /// </summary>
        public void Increment()
        {
            Interlocked.Increment(ref this.count);
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

        /// <summary>
        ///  Increment the reference count and create a <see cref="MemoryOwner"/> object by specifying the index and length.
        /// </summary>
        /// <param name="start">The index at which to begin the slice.</param>
        /// <param name="length">The number of elements to include in the slice.</param>
        /// <returns><see cref="MemoryOwner"/> object.</returns>
        public MemoryOwner IncrementAndShare(int start, int length)
        {
            Interlocked.Increment(ref this.count);
            return new MemoryOwner(this, start, length);
        }

        /// <summary>
        /// Decrement the reference count.<br/>
        /// When it reaches zero, it returns the byte array to the pool.<br/>
        /// Failure to return a rented array is not a fatal error (eventually be garbage-collected).
        /// </summary>
        /// <returns><see langword="null"></see>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Owner? Return()
        {
            var count = Interlocked.Decrement(ref this.count);
            if (count == 0 && this.bucket != null)
            {
                if (this.bucket.MaxPool == 0 || this.bucket.Queue.Count < this.bucket.MaxPool)
                {
                    this.bucket.Queue.Enqueue(this);
                }
            }

            return null;
        }

        public void Dispose() => this.Return();

        /// <summary>
        /// Create a <see cref="MemoryOwner"/> object from <see cref="Owner"/>.
        /// </summary>
        /// <returns><see cref="MemoryOwner"/>.</returns>
        public MemoryOwner ToMemoryOwner() => new MemoryOwner(this);

        /// <summary>
        /// Create a <see cref="MemoryOwner"/> object by specifying the index and length.
        /// </summary>
        /// <param name="start">The index at which to begin the slice.</param>
        /// <param name="length">The number of elements to include in the slice.</param>
        /// <returns><see cref="MemoryOwner"/>.</returns>
        public MemoryOwner ToMemoryOwner(int start, int length) => new MemoryOwner(this, start, length);

        internal void SetCount1() => Volatile.Write(ref this.count, 1);

        /// <summary>
        /// Gets a rent byte array.
        /// </summary>
        public byte[] ByteArray { get; }

        /// <summary>
        /// Gets a value indicating whether the owner (byte array) is returned or not.
        /// </summary>
        public bool IsReturned => Volatile.Read(ref this.count) <= 0;

        /// <summary>
        /// Gets the reference count of the owner.
        /// </summary>
        public int Count => Volatile.Read(ref this.count);

        private Bucket? bucket;
        private int count;
    }

    /// <summary>
    /// Represents an owner of a byte array and a <see cref="Memory{T}"/> object.
    /// </summary>
    public readonly struct MemoryOwner : IDisposable
    {
        public static readonly MemoryOwner Empty = new((Owner?)null);

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryOwner"/> struct from a byte array.<br/>
        /// This is a feature for compatibility with <see cref="ByteArrayPool"/>, and the byte array will not be returned when <see cref="Return"/> is called.
        /// </summary>
        /// <param name="byteArray">A byte array (other than <see cref="ByteArrayPool"/>).</param>
        public MemoryOwner(byte[] byteArray)
        {
            this.Owner = new(byteArray);
            this.Memory = byteArray.AsMemory();
        }

        internal MemoryOwner(Owner? owner)
        {
            this.Owner = owner;
            this.Memory = owner == null ? default : owner.ByteArray.AsMemory();
        }

        internal MemoryOwner(Owner owner, int start, int length)
        {
            this.Owner = owner;
            this.Memory = owner.ByteArray.AsMemory(start, length);
        }

        internal MemoryOwner(Owner owner, Memory<byte> memory)
        {
            this.Owner = owner;
            this.Memory = memory;
        }

        /// <summary>
        ///  Increment the reference count.
        /// </summary>
        public void Increment()
        {
            if (this.Owner == null)
            {
                throw new InvalidOperationException();
            }

            this.Owner.Increment();
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

        /// <summary>
        ///  Increment the reference count and create a <see cref="MemoryOwner"/> object by specifying the index and length.
        /// </summary>
        /// <param name="start">The index at which to begin the slice.</param>
        /// <param name="length">The number of elements to include in the slice.</param>
        /// <returns><see cref="MemoryOwner"/> object.</returns>
        public MemoryOwner IncrementAndShare(int start, int length)
        {
            if (this.Owner == null)
            {
                throw new InvalidOperationException();
            }

            return new(this.Owner.IncrementAndShare(), start, length);
        }

        /// <summary>
        /// Forms a slice out of the current memory that begins at a specified index.
        /// </summary>
        /// <param name="start">The index at which to begin the slice.</param>
        /// <returns><see cref="MemoryOwner"/>.</returns>
        public MemoryOwner Slice(int start)
            => new(this.Owner!, this.Memory.Slice(start));

        /// <summary>
        /// Forms a slice out of the current memory starting at a specified index for a specified length.
        /// </summary>
        /// <param name="start">The index at which to begin the slice.</param>
        /// <param name="length">The number of elements to include in the slice.</param>
        /// <returns><see cref="MemoryOwner"/>.</returns>
        public MemoryOwner Slice(int start, int length)
            => new(this.Owner!, this.Memory.Slice(start, length));

        /// <summary>
        /// Decrement the reference count.<br/>
        /// When it reaches zero, it returns the byte array to the pool.<br/>
        /// Failure to return a rented array is not a fatal error (eventually be garbage-collected).
        /// </summary>
        /// <returns><see langword="default"></see>.</returns>
        public MemoryOwner Return()
        {
            this.Owner?.Return();
            return default;
        }

        public void Dispose() => this.Return();

        /// <summary>
        /// Gets a value indicating whether the owner (byte array) is returned or not.
        /// </summary>
        public bool IsReturned => this.Owner == null || this.Owner.IsReturned == true;

        /// <summary>
        /// Gets a value indicating whether the memory is empty.
        /// </summary>
        public bool IsEmpty => this.Memory.IsEmpty;

        public readonly Owner? Owner;
        public readonly Memory<byte> Memory;
    }

    internal sealed class Bucket
    {
        public Bucket(ByteArrayPool pool, int arrayLength, int maxPool)
        {
            this.pool = pool;
            this.ArrayLength = arrayLength;
            this.MaxPool = maxPool;
        }

        public int ArrayLength { get; }

        public int MaxPool { get; private set; }

        internal void SetMaxPool(int maxPool)
        {
            this.MaxPool = maxPool;
        }

#pragma warning disable SA1401 // Fields should be private
        internal ConcurrentQueue<Owner> Queue = new();
#pragma warning restore SA1401 // Fields should be private

        private ByteArrayPool pool;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ByteArrayPool"/> class.<br/>
    /// </summary>
    /// <param name="maxLength">The maximum length of a byte array instance that may be stored in the pool (0 for max 1GB).</param>
    /// <param name="maxPool">The maximum number of array instances that may be stored in each bucket in the pool (0 for unlimited).</param>
    public ByteArrayPool(int maxLength = 0, int maxPool = 100)
    {
        if (maxLength <= 0 || maxLength > UpperBoundLength)
        {
            maxLength = UpperBoundLength;
        }

        var leadingZero = BitOperations.LeadingZeroCount((uint)maxLength - 1);
        var lowerBound = 32 - LowerBoundBits;
        if (leadingZero > lowerBound)
        {
            leadingZero = lowerBound;
        }
        else if (leadingZero < 2)
        {
            leadingZero = 2;
        }

        this.MaxLength = 1 << (32 - leadingZero);
        this.MaxPool = maxPool >= 0 ? maxPool : 0;

        this.buckets = new Bucket[33];
        for (var i = 0; i <= 32; i++)
        {
            if (i < leadingZero)
            {
                this.buckets[i] = null;
            }
            else if (i > lowerBound)
            {
                this.buckets[i] = this.buckets[lowerBound];
            }
            else
            {
                this.buckets[i] = new(this, 1 << (32 - i), this.MaxPool);
            }
        }
    }

    /// <summary>
    /// Gets a byte array from the pool or allocate a new byte array if not available.<br/>
    /// </summary>
    /// <param name="minimumLength">The minimum length of the byte array.</param>
    /// <returns>A rent byte array.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Owner Rent(int minimumLength)
    {
        var bucket = this.buckets[BitOperations.LeadingZeroCount((uint)minimumLength - 1)];
        if (bucket == null)
        {
            if (minimumLength == 0)
            {
                bucket = this.buckets[32]!;
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(minimumLength));
            }
        }

        if (!bucket.Queue.TryDequeue(out var owner))
        {// Allocate a new byte array.
            return new Owner(bucket);
        }

        owner.SetCount1();
        return owner;
    }

    /// <summary>
    /// Sets the maximum number of pooled byte arrays in the bucket corresponding to the specified size.
    /// </summary>
    /// <param name="length">The length of a byte array.</param>
    /// <param name="maxPool">The maximum number of array instances that may be stored in the bucket (0 for unlimited).</param>
    public void SetMaxPool(int length, int maxPool)
    {
        var bucket = this.buckets[BitOperations.LeadingZeroCount((uint)length - 1)];
        if (bucket == null)
        {
            if (length == 0)
            {
                bucket = this.buckets[32]!;
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
        }

        bucket.SetMaxPool(maxPool);
    }

    public void Dump(ISimpleLogger logger)
    {
        var sb = new StringBuilder();
        for (var i = 32; i >= 0; i--)
        {
            var b = this.buckets[i];
            if (b == null || b.Queue.Count == 0)
            {
                continue;
            }

            sb.Append($"{b.Queue.Count}({b.ArrayLength}) ");
        }

        logger.Information(sb.ToString());
    }

    /// <summary>
    /// Gets the maximum length of a byte array instance that may be stored in the pool.
    /// </summary>
    public int MaxLength { get; }

    /// <summary>
    /// Gets the maximum number of array instances that may be stored in each bucket in the pool.
    /// </summary>
    public int MaxPool { get; }

    private Bucket?[] buckets;
}
