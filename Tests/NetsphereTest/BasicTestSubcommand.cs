// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Arc.Crypto;
using LP.Subcommands;
using Netsphere;
using SimpleCommandLine;
using Tinyhand;

namespace NetsphereTest;

[SimpleCommand("basic")]
public class BasicTestSubcommand : ISimpleCommandAsync<BasicTestOptions>
{
    public BasicTestSubcommand(NetControl netControl)
    {
        this.NetControl = netControl;
    }

    public async Task Run(BasicTestOptions options, string[] args)
    {
        if (!SubcommandService.TryParseNodeAddress(options.Node, out var node))
        {
            return;
        }

        Logger.Priority.Information($"SendData: {node.ToString()}");
        Logger.Priority.Information($"{Stopwatch.Frequency}");

        // var nodeInformation = NodeInformation.Alternative;
        using (var terminal = this.NetControl.Terminal.Create(node))
        {
            // await terminal.SendAndReceiveAsync<PacketPunch, PacketPunchResponse>(new PacketPunch());

            var p = new PacketPunch(null);

            var sw = Stopwatch.StartNew();
            /*var t = terminal.SendAndReceiveAsync<PacketPunch, PacketPunchResponse>(p);
            Logger.Priority.Information($"t: {t.Result}");
            Logger.Priority.Information($"{sw.ElapsedMilliseconds} ms, Resend: {terminal.ResendCount}");*/

            /*sw.Restart();
            var t5 = terminal.SendPacketAndReceiveAsync<TestPacket, TestPacket>(TestPacket.Create(11));
            Logger.Priority.Information($"t5: {t5.Result}");
            Logger.Priority.Information($"{sw.ElapsedMilliseconds} ms, Resend: {terminal.ResendCount}");

            var p2 = TestBlock.Create(4_000_00);
            Logger.Priority.Information($"p2 send: {p2}");
            sw.Restart();
            var t2 = await terminal.SendAndReceiveAsync<TestBlock, TestBlock>(p2);

            p2 = TestBlock.Create(2000);
            Logger.Priority.Information($"p2b send: {p2}");
            var t3 = await terminal.SendAndReceiveAsync<TestBlock, TestBlock>(p2);
            Logger.Priority.Information($"t2 received: {t2.Value}");
            Logger.Priority.Information($"t3 received: {t3.Value}");
            Logger.Priority.Information($"{sw.ElapsedMilliseconds} ms, Resend: {terminal.ResendCount}");*/

            /*var p4 = TestBlock.Create(4_000_000);
            Logger.Priority.Information($"4MB send: {p4}");
            sw.Restart();
            var t4 = await terminal.SendAndReceiveAsync<TestBlock, TestBlock>(p4, int.MaxValue);
            Logger.Priority.Information($"4MB received: {t4.Value}");
            Logger.Priority.Information($"{sw.ElapsedMilliseconds} ms, Resend: {terminal.ResendCount}");*/

            var testService = terminal.GetService<Netsphere.Design.ITestService>();
            /*Logger.Priority.Information((await testService.Increment(3)).ToString());
            Logger.Priority.Information(testService.Result.ToString());
            await testService.Send(1, 2);
            Logger.Priority.Information(testService.Result.ToString());
            await testService.Send2(2, 3);
            Logger.Priority.Information(testService.Result.ToString());*/

            // Multi-threaded

            // IMPORTANT!
            /*if (await terminal.EncryptConnectionAsync() != NetResult.Success)
            {
                return;
            }*/

            var tt1 = testService.Increment(3);
            // var tt2 = testService.Send(1, 2);
            // var tt3 = testService.Send2(2, 3);
            Logger.Priority.Information(tt1.ResponseAsync.Result.ToString());
            // Logger.Priority.Information(tt2.ResponseAsync.Result.ToString());
            // Logger.Priority.Information(tt3.ResponseAsync.Result.ToString());

            var res = await tt1.ResponseAsync;
            if (res.IsSuccess)
            {
                Logger.Priority.Information(res.ToString());
            }

            /*var p4 = TestBlock.Create(4000_000);
            Logger.Priority.Information($"4MB send: {p4}");
            sw.Restart();
            // var t4 = await terminal.SendAndReceiveAsync<TestBlock, TestBlock>(p4);
            // Logger.Priority.Information($"4MB received: {t4.Value}");
            var result = await terminal.SendAsync<TestBlock>(p4);
            Logger.Priority.Information(result.ToString());
            Logger.Priority.Information($"{sw.ElapsedMilliseconds} ms, Resend: {terminal.ResendCount}");*/
        }
    }

    public NetControl NetControl { get; set; }
}

public record BasicTestOptions
{
    [SimpleOption("node", description: "Node address", Required = true)]
    public string Node { get; init; } = string.Empty;

    public override string ToString() => $"{this.Node}";
}
