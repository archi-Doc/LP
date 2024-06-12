﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Unit;
using Netsphere.Packet;
using Netsphere.Relay;
using SimpleCommandLine;

namespace Netsphere.Version;

[SimpleCommand("get")]
internal class GetCommand : ISimpleCommandAsync<GetOptions>
{
    public GetCommand(ILogger<GetCommand> logger, NetTerminal netTerminal)
    {
        this.logger = logger;
        // this.unit = unit;
        this.netTerminal = netTerminal;
    }

    public async Task RunAsync(GetOptions options, string[] args)
    {
        this.logger.TryGet()?.Log($"{options.ToString()}");

        if (!NetAddress.TryParse(options.Address, out var address))
        {
            return;
        }

        // var p = new PingPacket("test56789");
        // var result = await this.netTerminal.PacketTerminal.SendAndReceive<PingPacket, PingPacketResponse>(address, p);
        var p = new GetVersionPacket();
        var result = await this.netTerminal.PacketTerminal.SendAndReceive<GetVersionPacket, GetVersionResponse>(address, p);
        this.logger.TryGet()?.Log($"{result.ToString()}");
    }

    /*public async Task RunAsync(GetOptions options, string[] args)
    {
        this.logger.TryGet()?.Log($"{options.ToString()}");

        if (!NetNode.TryParseNetNode(this.logger, options.Node, out var node))
        {
            this.logger.TryGet(LogLevel.Fatal)?.Log($"Cannot parse node: {options.Node}");
            return;
        }

        var netOptions = new NetOptions() with
        {
        };

        await this.unit.Run(netOptions, false);

        var p = new PingPacket("test56789");
        var netTerminal = this.unit.Context.ServiceProvider.GetRequiredService<NetTerminal>();
        var result = await netTerminal.PacketTerminal.SendAndReceive<PingPacket, PingPacketResponse>(node.Address, p);

        await this.unit.Terminate();
    }*/

    private readonly ILogger logger;
    // private readonly ProgramUnit.Unit unit;
    private readonly NetTerminal netTerminal;
}
