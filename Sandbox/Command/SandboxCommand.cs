﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Arc.Unit;
using Netsphere;
using Netsphere.Misc;
using SimpleCommandLine;

namespace Sandbox;

[SimpleCommand("sandbox")]
public class SandboxCommand : ISimpleCommandAsync
{
    public SandboxCommand(ILogger<SandboxCommand> logger, NetControl netControl)
    {
        this.logger = logger;
        this.NetControl = netControl;
    }

    public async Task RunAsync(string[] args)
    {
        this.NetControl.Responders.Register(Netsphere.Responder.MemoryResponder.Instance);
        this.NetControl.Responders.Register(Netsphere.Responder.TestBlockResponder.Instance);

        var sw = Stopwatch.StartNew();
        var netTerminal = this.NetControl.NetTerminal;
        var packetTerminal = netTerminal.PacketTerminal;

        var netNode = await netTerminal.UnsafeGetNetNode(NetAddress.Alternative);
        if (netNode is null)
        {
            return;
        }

        // netTerminal.PacketTerminal.MaxResendCount = 0;
        using (var connection = await netTerminal.Connect(netNode))
        {
            if (connection is not null)
            {
                var testBlock = NetTestBlock.Create(1000);
                var r = await connection.SendAndReceive<NetTestBlock, NetTestBlock>(testBlock);
                Debug.Assert(testBlock.Equals(r.Value));
            }
        }
    }

    public NetControl NetControl { get; set; }

    private ILogger<SandboxCommand> logger;
}
