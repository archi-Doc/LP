﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Threading;
using Arc.Unit;
using BigMachines;
using LP;
using LP.NetServices;
using Netsphere;
using SimpleCommandLine;

namespace LPRunner;

[SimpleCommand("run", Default = true)]
public class ConsoleCommand : ISimpleCommandAsync
{
    public ConsoleCommand(ILogger<ConsoleCommand> logger, BigMachine bigMachine, NetControl netControl)
    {
        this.logger = logger;
        this.bigMachine = bigMachine;
        this.netControl = netControl;

        this.netControl.Services.Register<IRemoteControlService>();
    }

    public async Task RunAsync(string[] args)
    {
        var runner = this.bigMachine.RunnerMachine.Get();
        this.bigMachine.Start(ThreadCore.Root);

        while (!((IBigMachine)this.bigMachine).Core.IsTerminated)
        {
            if (!((IBigMachine)this).CheckActiveMachine())
            {
                break;
            }
            else
            {
                await ((IBigMachine)this.bigMachine).Core.WaitForTerminationAsync(1000);
            }
        }

        // await this.bigMachine.Core.WaitForTerminationAsync(-1);
        // await this.runner.Run();
    }

    private ILogger<ConsoleCommand> logger;
    private BigMachine bigMachine;
    private NetControl netControl;
}
