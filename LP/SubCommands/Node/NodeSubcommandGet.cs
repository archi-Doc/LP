﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;
using SimpleCommandLine;

namespace LP.Subcommands;

[SimpleCommand("get")]
public class NodeSubcommandGet : ISimpleCommandAsync<NodeSubcommandGetOptions>
{
    public NodeSubcommandGet(ILogger<NodeSubcommandGet> logger, Control control)
    {
        this.Control = control;
        this.logger = logger;
    }

    public async Task RunAsync(NodeSubcommandGetOptions options, string[] args)
    {
        if (!NetAddress.TryParse(this.logger, options.Node, out var address))
        {
            return;
        }

        using (var terminal = this.Control.NetControl.TerminalObsolete.TryCreate(address))
        {
            if (terminal is null)
            {
                return;
            }

            var p = new PacketGetNodeInformationObsolete();
            var result = await terminal.SendPacketAndReceiveAsync<PacketGetNodeInformationObsolete, PacketGetNodeInformationResponseObsolete>(p);
            if (result.Value != null)
            {
                var node = new NetNode(address, result.Value.Node.PublicKey);
                this.logger.TryGet()?.Log($"{node.ToString()}");
            }
        }
    }

    public Control Control { get; set; }

    private ILogger logger;
}

public record NodeSubcommandGetOptions
{
    [SimpleOption("node", Description = "Node address", Required = true)]
    public string Node { get; init; } = string.Empty;
}
