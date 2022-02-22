﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Crypto;
using LP;
using SimpleCommandLine;
using ZenItz;

namespace LP.Subcommands;

[SimpleCommand("ls", Description = "List zen directory information.")]
public class ZenSubcommandLs : ISimpleCommandAsync
{
    public ZenSubcommandLs(ZenControl control)
    {
        this.Control = control;
    }

    public async Task Run(string[] args)
    {
        var info = this.Control.Zen.IO.GetDirectoryInformation();

        foreach (var x in info)
        {
            Console.WriteLine(x);
        }
    }

    public ZenControl Control { get; set; }
}
