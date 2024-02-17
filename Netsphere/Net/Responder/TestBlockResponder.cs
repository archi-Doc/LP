﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Block;
using Netsphere.Server;

namespace Netsphere.Responder;

public class TestBlockResponder : SyncResponder<NetTestBlock, NetTestBlock>
{
    public static readonly INetResponder Instance = new TestBlockResponder();

    public override NetTestBlock? RespondSync(NetTestBlock value)
        => value;
}
