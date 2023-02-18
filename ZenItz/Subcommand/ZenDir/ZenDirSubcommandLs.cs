﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;
using ZenItz;

namespace CrystalData.Subcommands;

[SimpleCommand("ls", Description = "List zen directory information.")]
public class ZenDirSubcommandLs : ISimpleCommandAsync
{
    public ZenDirSubcommandLs(IConsoleService consoleService, ZenControl zenControl)
    {
        this.consoleService = consoleService;
        this.zenControl = zenControl;
    }

    public async Task RunAsync(string[] args)
    {
        var info = this.zenControl.Zen.Storage.GetDirectoryInformation();

        foreach (var x in info)
        {
            this.consoleService.WriteLine(x.ToString());
        }
    }

    private IConsoleService consoleService;
    private ZenControl zenControl;
}
