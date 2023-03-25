﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace CrystalData.Filer;

internal class FilerWork : IEquatable<FilerWork>
{
    public enum WorkType
    {
        Write,
        Read,
        Delete,
    }

    public WorkType Type { get; }

    public CrystalResult Result { get; internal set; } = CrystalResult.NotStarted;

    public string Path { get; }

    public long Offset { get; }

    public int Length { get; }

    public ByteArrayPool.ReadOnlyMemoryOwner WriteData { get; }

    public ByteArrayPool.MemoryOwner ReadData { get; internal set; }

    public FilerWork(string path, long offset, ByteArrayPool.ReadOnlyMemoryOwner dataToBeShared)
    {// Write
        this.Type = WorkType.Write;
        this.Path = path;
        this.Offset = offset;
        this.WriteData = dataToBeShared.IncrementAndShare();
    }

    public FilerWork(string path, long offset, int length)
    {// Read
        this.Type = WorkType.Read;
        this.Path = path;
        this.Offset = offset;
        this.Length = length;
    }

    public FilerWork(string path)
    {// Delete
        this.Type = WorkType.Delete;
        this.Path = path;
    }

    public override int GetHashCode()
        => HashCode.Combine(this.Type, this.Path, this.WriteData.Memory.Length, this.Length);

    public bool Equals(FilerWork? other)
    {
        if (other == null)
        {
            return false;
        }

        return this.Type == other.Type &&
            this.Path == other.Path &&
            this.WriteData.Memory.Span.SequenceEqual(other.WriteData.Memory.Span) &&
            this.Length == other.Length;
    }
}
