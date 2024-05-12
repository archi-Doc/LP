﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Unit;
using Netsphere.Interfaces;

namespace Netsphere.Runner;

[NetServiceObject]
internal class RemoteControlAgent : IRemoteControl
{// Remote -> Netsphere.Runner
    public RemoteControlAgent(ILogger<RemoteControlAgent> logger, BigMachine bigMachine, RunOptions runOptions)
    {
        this.logger = logger;
        this.bigMachine = bigMachine;
        this.runOptions = runOptions;
    }

    /*public async NetTask Authenticate(AuthenticationToken token)
    {
        if (TransmissionContext.Current.ServerConnection.ValidateAndVerifyWithSalt(token) &&
            token.PublicKey.Equals(this.runOptions.RemotePublicKey))
        {
            this.token = token;
            TransmissionContext.Current.Result = NetResult.Success;
            return;
        }

        TransmissionContext.Current.Result = NetResult.NotAuthorized;
    }*/

    public async NetTask<NetResult> Restart()
    {
        if (!TransmissionContext.Current.TryGetAuthenticationToken(out var token) ||
            token.PublicKey.Equals(this.runOptions.RemotePublicKey))
        {
            return NetResult.NotAuthorized;
        }

        var machine = this.bigMachine.RunnerMachine.GetOrCreate();
        if (machine != null)
        {
            _ = machine.Command.Restart();
        }

        return NetResult.Success;

        /*var address = this.information.TryGetDualAddress();
        if (!address.IsValid)
        {
            return NetResult.NoNodeInformation;
        }

        var netTerminal = this.netControl.NetTerminal;
        var netNode = await netTerminal.UnsafeGetNetNode(address);
        if (netNode is null)
        {
            return NetResult.NoNodeInformation;
        }

        using (var terminal = await netTerminal.Connect(netNode))
        {
            if (terminal is null)
            {
                return NetResult.NoNetwork;
            }

            var remoteControl = terminal.GetService<IRemoteControl>();
            var response = await remoteControl.Authenticate(this.token).ResponseAsync;
            this.logger.TryGet()?.Log($"RequestAuthorization: {response.Result}");
            if (!response.IsSuccess)
            {
                return NetResult.NotAuthorized;
            }

            var result = await remoteControl.Restart();
            this.logger.TryGet()?.Log($"Restart: {result}");
            if (result == NetResult.Success)
            {
                var machine = this.bigMachine.RunnerMachine.GetOrCreate();
                if (machine != null)
                {
                    _ = machine.Command.Restart();
                }
            }

            return result;
        }*/
    }

    private readonly ILogger logger;
    private readonly BigMachine bigMachine;
    private readonly RunOptions runOptions;
}