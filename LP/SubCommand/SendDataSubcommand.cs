﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Arc.Crypto;
using LP;
using LP.Net;
using SimpleCommandLine;
using Tinyhand;

namespace LP.Subcommands;

[SimpleCommand("senddata")]
public class SendDataSubcommand : ISimpleCommandAsync<SendDataOptions>
{
    public SendDataSubcommand(Control control)
    {
        this.Control = control;
    }

    public async Task Run(SendDataOptions options, string[] args)
    {
        if (!SubcommandService.TryParseNodeAddress(options.Node, out var node))
        {
            return;
        }

        Logger.Priority.Information($"SendData: {node.ToString()}");

        var nodeInformation = NodeInformation.Alternative;
        using (var terminal = this.Control.Netsphere.Terminal.Create(nodeInformation))
        {
            var p = new RawPacketPunch(null);
            var netInterface = terminal.SendAndReceive<RawPacketPunch, RawPacketPunchResponse>(p);
            if (netInterface != null)
            {
                netInterface.Receive(out var r);
            }
        }
    }

    public Control Control { get; set; }
}

public record SendDataOptions
{
    [SimpleOption("node", description: "Node address", Required = true)]
    public string Node { get; init; } = string.Empty;

    public override string ToString() => $"{this.Node}";
}
