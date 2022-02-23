﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using SimpleCommandLine;
using Tinyhand;
using LP.Fragments;

namespace ZenItzTest;

[SimpleCommand("zentest")]
public class ZenTestSubcommand : ISimpleCommandAsync<ZenTestOptions>
{
    public ZenTestSubcommand(ZenControl zenControl)
    {
        this.ZenControl = zenControl;
    }

    public async Task Run(ZenTestOptions options, string[] args)
    {
        var zen = this.ZenControl.Zen;
        var itz = this.ZenControl.Itz;

        await zen.TryStartZen(new(Zen.DefaultZenDirectory));
        var p = zen.TryCreateOrGet(Identifier.Zero);
        if (p != null)
        {
            p.Set(new byte[] { 0, 1, });
            p.Save(true);
            var result = await p.Get();
            p.Remove();
        }

        p = zen.TryCreateOrGet(Identifier.One);
        if (p != null)
        {
            p.SetObject(new TestFragment());
            var t = await p.GetObject<TestFragment>();

            p.Save(true);
            t = await p.GetObject<TestFragment>();
        }

        await zen.StopZen(new());
    }

    public ZenControl ZenControl { get; set; }
}

public record ZenTestOptions
{
    // [SimpleOption("node", description: "Node address", Required = true)]
    // public string Node { get; init; } = string.Empty;

    public override string ToString() => $"";
}
