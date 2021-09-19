﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Arc.Collection;
using Arc.Crypto;

namespace LP;

public class Hash : SHA3_256
{
    public static LooseObjectPool<Hash> ObjectPool { get; } = new(static () => new Hash());

    public Identifier GetIdentifier(ReadOnlySpan<byte> input)
    {
        return new Identifier(this.GetHashULong(input));
    }

    public Identifier IdentifierFinal()
    {
        return new Identifier(this.HashFinalULong());
    }
}
