// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;
using ZenItz;

namespace ZenItz.Subcommands;

[SimpleCommand("ls", Description = "List zen directory information.")]
public class ZenTempSubcommandLs : ISimpleCommandAsync
{
    public ZenTempSubcommandLs(ZenControl control)
    {
        this.Control = control;
    }

    public async Task RunAsync(string[] args)
    {
        var info = this.Control.Zen.IO.GetDirectoryInformation();

        foreach (var x in Enumerable.Range(0, 5))
        {
            Console.WriteLine(x.ToString());
        }
    }

    public ZenControl Control { get; set; }
}
