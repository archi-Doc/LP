﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Unit;
using BigMachines;
using Docker.DotNet;
using Docker.DotNet.Models;
using LP;
using LP.NetServices;
using Netsphere;
using Tinyhand;

namespace LPRunner;

[MachineObject(0x0b5190d7, Group = typeof(SingleGroup<Identifier>))]
public partial class RunnerMachine : Machine<Identifier>
{
    public enum LPStatus
    {
        NoContainer,
        Container,
        Running,
    }

    public RunnerMachine(ILogger<RunnerMachine> logger, BigMachine<Identifier> bigMachine, LPBase lPBase, NetControl netControl, RunnerInformation information)
        : base(bigMachine)
    {
        this.logger = logger;
        this.lpBase = lPBase;
        this.netControl = netControl;
        this.Information = information;

        this.DefaultTimeout = TimeSpan.FromSeconds(1);
    }

    [StateMethod(0)]
    protected async Task<StateResult> Initial(StateParameter parameter)
    {
        this.docker = DockerRunner.Create(this.logger, this.Information);
        if (this.docker == null)
        {
            this.logger.TryGet(LogLevel.Fatal)?.Log($"No docker");
            return StateResult.Terminate;
        }

        this.logger.TryGet()?.Log($"Runner start");
        this.logger.TryGet()?.Log($"Root directory: {this.lpBase.RootDirectory}");
        var nodeInformation = this.netControl.NetStatus.GetMyNodeInformation(false);
        this.logger.TryGet()?.Log($"Port: {nodeInformation.Port}, Public key: ({nodeInformation.PublicKey.ToString()})");
        this.logger.TryGet()?.Log($"{this.Information.ToString()}");
        this.logger.TryGet()?.Log("Press Ctrl+C to exit.");
        await Console.Out.WriteLineAsync();

        // Remove container
        await this.docker.RemoveContainer();

        this.ChangeState(State.Check, true);
        return StateResult.Continue;
    }

    [StateMethod(1)]
    protected async Task<StateResult> Check(StateParameter parameter)
    {
        if (this.docker == null)
        {
            return StateResult.Terminate;
        }

        if (this.checkRetry > 10)
        {
            return StateResult.Terminate;
        }

        this.logger.TryGet()?.Log($"Check ({this.checkRetry++})");
        var status = await this.GetLPStatus();
        this.logger.TryGet()?.Log($"Status: {status}");

        if (status == LPStatus.Running)
        {// Running
            this.checkRetry = 0;
            this.ChangeState(State.Running);
            return StateResult.Continue;
        }
        else if (status == LPStatus.NoContainer)
        {// No container -> Run
            if (await this.docker.RunContainer() == false)
            {
                return StateResult.Terminate;
            }

            this.SetTimeout(TimeSpan.FromSeconds(30));
            return StateResult.Continue;
        }
        else
        {// Container -> Try restart
            await this.docker.RestartContainer();
            this.SetTimeout(TimeSpan.FromSeconds(10));
            return StateResult.Continue;
        }
    }

    [StateMethod(3)]
    protected async Task<StateResult> Running(StateParameter parameter)
    {
        /*var result = await this.SendAcknowledge();
        this.logger.TryGet()?.Log($"Running: {result}");
        if (result != NetResult.Success)
        {
            this.ChangeState(State.Check);
        }*/

        this.SetTimeout(TimeSpan.FromSeconds(10));
        return StateResult.Continue;
    }

    [CommandMethod(0)]
    protected async Task Restart()
    {
        this.logger.TryGet()?.Log("RemoteControl -> Restart");

        // Remove container
        if (this.docker != null)
        {
            await this.docker.RemoveContainer();
        }

        this.ChangeState(State.Check);
    }

    public RunnerInformation Information { get; private set; }

    private async Task<LPStatus> GetLPStatus()
    {
        if (await this.SendAcknowledge() == NetResult.Success)
        {
            return LPStatus.Running;
        }

        if (this.docker == null)
        {
            return LPStatus.NoContainer;
        }

        var containers = await this.docker.EnumerateContainersAsync();
        if (containers.Count() > 0)
        {
            return LPStatus.Container;
        }

        return LPStatus.NoContainer;
    }

    private async Task<NetResult> SendAcknowledge()
    {
        var nodeAddress = this.Information.TryGetNodeAddress();
        if (nodeAddress == null)
        {
            return NetResult.NoNodeInformation;
        }

        using (var terminal = this.netControl.Terminal.Create(nodeAddress))
        {
            var result = await terminal.SendAndReceiveAsync<PacketPing, PacketPingResponse>(new());
            this.logger.TryGet()?.Log($"Ping: {result.Result}");
            return result.Result;
        }
    }

    private ILogger<RunnerMachine> logger;
    private LPBase lpBase;
    private NetControl netControl;
    private DockerRunner? docker;
    private int checkRetry;
}