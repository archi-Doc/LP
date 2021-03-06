// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Arc.Crypto;
using LP.Block;
using LP.Data;
using SimpleCommandLine;
using Tinyhand;

namespace LP.Subcommands.Dump;

[SimpleCommand("ls")]
public class FlagsSubcommandLs : ISimpleCommand
{// flags on name
    public FlagsSubcommandLs(Control control)
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

        List<string> on = new();
        List<string> off = new();
        var names = ope.GetNames();
        foreach (var x in names)
        {
            if (ope.TryGet<bool>(x, out var value))
            {
                if (value)
                {
                    on.Add(x);
                }
                else
                {
                    off.Add(x);
                }
            }
        }

        if (on.Count > 0)
        {
            Logger.Default.Information($"On: {string.Join(' ', on)}");
        }

        if (off.Count > 0)
        {
            Logger.Default.Information($"Off: {string.Join(' ', off)}");
        }
    }

    public Control Control { get; set; }
}
