﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using CrystalData.Filer;
using Tinyhand.IO;

namespace CrystalData.Journal;

public partial class SimpleJournal : IJournalInternal
{
    public const int MaxRecordLength = 1024 * 16; // 16 KB
    public const int MaxBookLength = 1024 * 1024 * 16; // 16 MB
    public const int MaxMemoryLength = 1024 * 1024 * 64; // 64 MB
    public const string BookSuffix = ".book";

    [ThreadStatic]
    private static byte[] initialBuffer = new byte[1024 * 16]; // 16 KB

    public SimpleJournal(Crystalizer crystalizer, SimpleJournalConfiguration configuration)
    {
        this.crystalizer = crystalizer;
        this.SimpleJournalConfiguration = configuration;
    }

    public async Task<CrystalStartResult> Prepare(Crystalizer crystalizer)
    {
        var configuration = this.SimpleJournalConfiguration.DirectoryConfiguration;

        this.rawFiler ??= this.crystalizer.ResolveRawFiler(configuration);
        var result = await this.rawFiler.PrepareAndCheck(crystalizer, configuration).ConfigureAwait(false);
        if (result != CrystalResult.Success)
        {
            return CrystalStartResult.FileError;
        }

        // List journal books
        var list = await this.rawFiler.ListAsync(configuration.Path, "*" + BookSuffix).ConfigureAwait(false);

        return CrystalStartResult.Success;
    }

    void IJournal.GetJournalWriter(JournalRecordType recordType, out TinyhandWriter writer)
    {
        writer = new(initialBuffer);
        writer.Write(Unsafe.As<JournalRecordType, byte>(ref recordType));
    }

    ulong IJournal.AddRecord(in TinyhandWriter writer)
    {
        writer.FlushAndGetMemory(out var memory, out var useInitialBuffer);
        writer.Dispose();

        if (memory.Length > MaxRecordLength)
        {
            throw new InvalidOperationException($"The maximum length per record is {MaxRecordLength} bytes.");
        }

        lock (this.syncObject)
        {
            SimpleJournalBook book = this.EnsureBook();
            var waypoint = book.AppendData(memory.Span);
        }

        return 0;
    }

    public SimpleJournalConfiguration SimpleJournalConfiguration { get; }

    public bool Prepared { get; private set; }

    private Crystalizer crystalizer;
    private IRawFiler? rawFiler;

    // Journal
    private object syncObject = new();
    private SimpleJournalBook.GoshujinClass books = new();
    private ulong memoryUsage;

    private SimpleJournalBook EnsureBook()
    {// lock (this.syncJournal)
        SimpleJournalBook book;

        if (this.books.Count == 0)
        {// Empty
            book = SimpleJournalBook.AppendNewBook(this, null);
        }
        else
        {
            book = this.books.BookStartChain.Last!;
            book.EnsureBuffer(MaxRecordLength);
        }

        return book;
    }
}