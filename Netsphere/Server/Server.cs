// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

namespace Netsphere;

public class Server
{
    public Server(NetBase netBase, NetControl netControl, NetService netService)
    {
        this.NetBase = netBase;
        this.NetControl = netControl;
        this.NetService = netService;
    }

    public async Task Process(ServerTerminal terminal)
    {
        this.NetTerminal = terminal;
        while (!this.NetTerminal.IsClosed)
        {
            try
            {
                var received = await terminal.ReceiveAsync();
                if (received.Result == NetResult.Success)
                {// Success
                    if (received.PacketId == PacketId.Data &&
                        this.NetControl.Responders.TryGetValue(received.DataId, out var responder) &&
                        responder.Respond(terminal, received))
                    {// Responder
                        continue;
                    }
                    else if (received.PacketId == PacketId.Rpc)
                    {// RPC
                        await this.NetService.Process(terminal, received).ConfigureAwait(false);
                        continue;
                    }

                    // Essential (PacketPunch)
                    if (this.ProcessEssential(received))
                    {
                        continue;
                    }
                }
                else if (received.Result == NetResult.Timeout ||
                    received.Result == NetResult.NoReceiver)
                {
                    await this.NetTerminal.SendClose();
                    break;
                }
                else if (received.Result == NetResult.Closed)
                {
                    break;
                }
            }
            finally
            {
                await terminal.ClearSender();
            }
        }

        terminal.TerminalLogger?.Information($"Server offline.");
    }

    public ThreadCoreBase? Core => this.NetControl.Terminal.Core;

    public NetBase NetBase { get; }

    public NetControl NetControl { get; }

    public NetService NetService { get; }

    public ServerTerminal NetTerminal { get; private set; } = default!;

    private bool ProcessEssential(NetReceivedData received)
    {
        if (received.PacketId == PacketId.Punch)
        {
            return this.ProcessEssential_Punch(received);
        }

        if (this.NetBase.NetsphereOptions.EnableTest)
        {
            if (received.PacketId == PacketId.Test)
            {
                return this.ProcessEssential_Test(received);
            }
        }

        return false;
    }

    private bool ProcessEssential_Punch(NetReceivedData received)
    {
        if (!TinyhandSerializer.TryDeserialize<PacketPunch>(received.Received.Memory, out var punch))
        {
            return false;
        }

        TimeCorrection.AddCorrection(punch.UtcMics);

        var response = new PacketPunchResponse();
        response.Endpoint = this.NetTerminal.Endpoint;
        response.UtcMics = Mics.GetUtcNow();

        var task = this.NetTerminal.SendPacketAsync(response);
        return true;
    }

    private bool ProcessEssential_Test(NetReceivedData received)
    {
        if (!TinyhandSerializer.TryDeserialize<TestPacket>(received.Received.Memory, out var r))
        {
            var task2 = this.NetTerminal.SendEmpty();
            return false;
        }

        var response = TestPacket.Create(2000);
        var task = this.NetTerminal.SendAsync(response);
        return true;
    }
}
