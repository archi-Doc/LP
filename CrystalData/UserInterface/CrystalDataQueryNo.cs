﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace CrystalData.UserInterface;

internal class CrystalDataQueryNo : ICrystalDataQuery
{
    Task<AbortOrContinue> ICrystalDataQuery.NoCheckFile()
        => Task.FromResult(AbortOrContinue.Continue);

    Task<AbortOrContinue> ICrystalDataQuery.InconsistentJournal(string path)
        => Task.FromResult(AbortOrContinue.Continue);
}
