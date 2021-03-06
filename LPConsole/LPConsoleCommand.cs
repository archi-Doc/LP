// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using LP.Data;
using SimpleCommandLine;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace LPConsole;

[SimpleCommand("lp", Default = true)]
public class LPConsoleCommand : ISimpleCommandAsync<LPOptions>
{
    public LPConsoleCommand(Control.Unit unit)
    {
        this.unit = unit;
    }

    public async Task RunAsync(LPOptions options, string[] args)
    {
        await this.unit.RunAsync(options);
    }

    private Control.Unit unit;
}
