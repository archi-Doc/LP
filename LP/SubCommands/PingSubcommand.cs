// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Arc.Crypto;
using LP;
using Netsphere;
using SimpleCommandLine;
using Tinyhand;

namespace LP.Subcommands;

[SimpleCommand("ping")]
public class PingSubcommand : ISimpleCommandAsync<PingOptions>
{
    public PingSubcommand(Control control)
    {
        this.Control = control;
    }

    public async Task RunAsync(PingOptions options, string[] args)
    {
        if (!SubcommandService.TryParseNodeAddress(options.Node, out var node))
        {
            return;
        }

        for (var n = 0; n < options.Count; n++)
        {
            if (this.Control.Core.IsTerminated)
            {
                break;
            }

            await this.Ping(node, options);

            if (n < options.Count - 1)
            {
                this.Control.Core.Sleep(TimeSpan.FromSeconds(options.Interval), TimeSpan.FromSeconds(0.1));
            }
        }
    }

    public async Task Ping(NodeAddress node, PingOptions options)
    {
        Logger.Default.Information($"Ping: {node.ToString()}");

        var sw = Stopwatch.StartNew();
        using (var terminal = this.Control.NetControl.Terminal.Create(node))
        {
            var p = new PacketPing("test");
            sw.Restart();
            var result = await terminal.SendPacketAndReceiveAsync<PacketPing, PacketPingResponse>(p);
            sw.Stop();
            if (result.Value != null)
            {
                Logger.Default.Information($"Received: {result.ToString()} - {sw.ElapsedMilliseconds} ms");
            }
            else
            {
                Logger.Default.Error($"{result}");
            }
        }

        // this.Control.Netsphere.NetStatus
        // Logger.Default.Information(System.Environment.OSVersion.ToString());
    }

    public Control Control { get; set; }
}

public record PingOptions
{
    [SimpleOption("node", description: "Node address", Required = true)]
    public string Node { get; init; } = string.Empty;

    [SimpleOption("count", description: "Count")]
    public int Count { get; init; } = 1;

    [SimpleOption("interval", description: "Interval (seconds)")]
    public int Interval { get; init; } = 2;

    public override string ToString() => $"{this.Node}";
}
