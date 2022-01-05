﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
public sealed class NetServiceObjectAttribute : Attribute
{
    public NetServiceObjectAttribute()
    {
    }
}
