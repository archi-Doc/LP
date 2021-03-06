// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Arc.Crypto;
using LP.Block;
using LP.Data;
using SimpleCommandLine;
using Tinyhand;

namespace LP.Subcommands.Dump;

[SimpleCommand("clear")]
public class FlagsSubcommandClear : ISimpleCommand
{// flags on name
    public FlagsSubcommandClear(Control control)
    {
        this.Control = control;
    }

    public void Run(string[] args)
    {
        var ope = VisceralClass.TryGet(this.Control.LPBase.Settings.Flags);
        if (ope == null)
        {
            return;
        }

        List<string> cleared = new();
        var names = ope.GetNames();
        foreach (var x in names)
        {
            if (ope.TryGet<bool>(x, out var value))
            {
                if (value)
                {
                    ope.TrySet(x, false);
                    cleared.Add(x);
                }
            }
        }

        if (cleared.Count > 0)
        {
            Logger.Default.Information($"Cleared: {string.Join(' ', cleared)}");
        }
    }

    public Control Control { get; set; }
}
