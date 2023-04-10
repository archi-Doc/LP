﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Tinyhand.IO;

namespace CrystalData.Journal;

public class EmptyJournal : IJournalInternal
{
    Task<CrystalStartResult> IJournal.Prepare(Crystalizer crystalizer)
    {
        this.Prepared = true;
        return Task.FromResult(CrystalStartResult.Success);
    }

    ulong IJournal.AddRecord(in TinyhandWriter writer)
    {
        return 0;
    }

    void IJournal.GetJournalWriter(JournalRecordType recordType, out TinyhandWriter writer)
    {
        writer = default(TinyhandWriter);
    }

    public bool Prepared { get; private set; }

    bool IJournal.Prepared => throw new NotImplementedException();
}