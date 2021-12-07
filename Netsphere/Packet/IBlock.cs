﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Netsphere;

public interface IBlock
{
    /// <summary>
    /// Gets an identifier of the block.
    /// </summary>
    public uint Id { get; }
}
