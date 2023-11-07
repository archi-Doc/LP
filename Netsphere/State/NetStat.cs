﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.State;

[TinyhandObject(UseServiceProvider = true)]
public partial class NetStat
{
    public NetStat(EssentialAddress essentialAddress)
    {
        this.EssentialAddress = essentialAddress;
    }

    #region FieldAndProperty

    [Key(0)]
    public EssentialAddress EssentialAddress { get; private set; }

    [Key(1)]
    public NodeType Ipv4State { get; private set; }

    [Key(2)]
    public NodeType Ipv6State { get; private set; }

    #endregion
}
