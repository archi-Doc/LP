﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

namespace LP.NetServices;

public class MergerOrTestFilter : IServiceFilter
{
    public MergerOrTestFilter(NetBase netBase, LpBase lpBase)
    {
        this.NetBase = netBase;
        this.LPBase = lpBase;
    }

    public async Task Invoke(TransmissionContext context, Func<TransmissionContext, Task> invoker)
    {
        if (this.LPBase.Options.TestFeatures)
        {// this.LPBase.Mode == LPMode.Merger
            await invoker(context);
        }
        else
        {
            context.Result = NetResult.NoNetService;
        }
    }

    public NetBase NetBase { get; }

    public LpBase LPBase { get; }
}