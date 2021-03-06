// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Arc.Crypto;
using LP;
using LP.Services;
using Netsphere;
using SimpleCommandLine;
using Tinyhand;

namespace LP.Subcommands;

[SimpleCommand("netbench")]
public class NetBenchSubcommand : ISimpleCommandAsync<NetBenchOptions>
{
    public NetBenchSubcommand(Control control)
    {
        this.Control = control;
        this.NetControl = control.NetControl;
    }

    public async Task RunAsync(NetBenchOptions options, string[] args)
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

            await this.Process(node, options);

            if (n < options.Count - 1)
            {
                this.Control.Core.Sleep(TimeSpan.FromSeconds(options.Interval), TimeSpan.FromSeconds(0.1));
            }
        }
    }

    public async Task Process(NodeAddress node, NetBenchOptions options)
    {
        Logger.Default.Information($"NetBench: {node.ToString()}");

        var sw = Stopwatch.StartNew();
        using (var terminal = this.Control.NetControl.Terminal.Create(node))
        {
            await this.SendLargeData(terminal);
            // await this.PingpongSmallData(terminal);
        }
    }

    public Control Control { get; set; }

    public NetControl NetControl { get; set; }

    private async Task SendLargeData(ClientTerminal terminal)
    {
        const int N = 4_000_000;
        var service = terminal.GetService<IBenchmarkService>();
        var data = new byte[N];

        var sw = Stopwatch.StartNew();
        var response = await service.Send(data).ResponseAsync;
        sw.Stop();

        Console.WriteLine(response);
        Console.WriteLine(sw.ElapsedMilliseconds.ToString());
    }

    private async Task PingpongSmallData(ClientTerminal terminal)
    {
        const int N = 20;
        var service = terminal.GetService<IBenchmarkService>();
        var data = new byte[100];

        var sw = Stopwatch.StartNew();
        ServiceResponse<byte[]?> response = default;
        var count = 0;
        for (var i = 0; i < N; i++)
        {
            response = await service.Pingpong(data).ResponseAsync;
            if (response.IsSuccess)
            {
                count++;
            }
        }

        sw.Stop();

        Console.WriteLine($"PingpongSmallData {count}/{N}, {sw.ElapsedMilliseconds.ToString()} ms");
        Console.WriteLine();
    }

    private async Task MassiveSmallData(NodeAddress node)
    {
        const int N = 50;
        var data = new byte[100];

        ThreadPool.GetMinThreads(out var workMin, out var ioMin);
        ThreadPool.SetMinThreads(50, ioMin);

        var sw = Stopwatch.StartNew();
        var count = 0;
        Parallel.For(0, N, i =>
        {
            for (var j = 0; j < 20; j++)
            {
                using (var terminal = this.NetControl.Terminal.Create(node))
                {
                    var service = terminal.GetService<IBenchmarkService>();
                    var response = service.Pingpong(data).ResponseAsync;
                    if (response.Result.IsSuccess)
                    {
                        Interlocked.Increment(ref count);
                    }
                    else
                    {
                        Console.WriteLine(response.Result.Result.ToString());
                    }
                }
            }
        });

        sw.Stop();

        Console.WriteLine(this.NetControl.Alternative?.MyStatus.ServerCount.ToString());
        Console.WriteLine($"MassiveSmallData {count}/{N}, {sw.ElapsedMilliseconds.ToString()} ms");
        Console.WriteLine();
    }
}

public record NetBenchOptions
{
    [SimpleOption("node", description: "Node address", Required = true)]
    public string Node { get; init; } = string.Empty;

    [SimpleOption("count", description: "Count")]
    public int Count { get; init; } = 1;

    [SimpleOption("interval", description: "Interval (seconds)")]
    public int Interval { get; init; } = 2;

    public override string ToString() => $"{this.Node}";
}
