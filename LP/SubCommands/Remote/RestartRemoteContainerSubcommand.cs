﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;
using Netsphere.Interfaces;
using Netsphere.Packet;
using SimpleCommandLine;

namespace LP.Subcommands;

[SimpleCommand("restart-remote-container")]
public class RestartRemoteContainerSubcommand : ISimpleCommandAsync<RestartRemoteContainerOptions>
{
    private const int WaitIntervalInSeconds = 5;
    private const int PingIntervalInSeconds = 1;
    private const int PingRetries = 5;

    public RestartRemoteContainerSubcommand(ILogger<RestartRemoteContainerSubcommand> logger, NetTerminal terminal)
    {
        this.logger = logger;
        this.netTerminal = terminal;
    }

    public async Task RunAsync(RestartRemoteContainerOptions options, string[] args)
    {
        if (await NetHelper.TryGetNetNode(this.netTerminal, options.Node) is not { } netNode)
        {
            return;
        }

        if (!CryptoHelper.TryParseFromSourceOrEnvironmentVariable<SignaturePrivateKey>(options.RemotePrivateKey, NetConstants.RemotePrivateKeyName, out var privateKey))
        {
            return;
        }

        // Ping container
        this.containerAddress = new(netNode.Address, options.ContainerPort);
        var r = await this.netTerminal.PacketTerminal.SendAndReceive<PingPacket, PingPacketResponse>(this.containerAddress, new());

        /*var authority = await this.authorityVault.GetAuthority(options.Authority);
        if (authority == null)
        {
            this.logger.TryGet(LogLevel.Error)?.Log(Hashed.Authority.NotFound, options.Authority);
            return;
        }*/

        // Restart
        using (var connection = await this.netTerminal.Connect(netNode))
        {
            if (connection == null)
            {
                this.logger.TryGet()?.Log(Hashed.Error.Connect, netNode.ToString());
                return;
            }

            var token = new AuthenticationToken(connection.Salt);
            NetHelper.Sign(token, privateKey);
            var result = await connection.Authenticate(token).ConfigureAwait(false);
            if (result != NetResult.Success)
            {
                this.logger.TryGet(LogLevel.Error)?.Log(Hashed.Error.Authorization);
                return;
            }

            var service = connection.GetService<IRemoteControl>();
            result = await service.Restart();
            this.logger.TryGet()?.Log($"Restart: {result}");
            if (result != NetResult.Success)
            {
                return;
            }
        }

        // Wait 5 seconds
        this.logger.TryGet()?.Log($"Waiting...");
        await Task.Delay(TimeSpan.FromSeconds(WaitIntervalInSeconds));

        // Ping container
    }

    private readonly ILogger logger;
    private readonly NetTerminal netTerminal;
    private NetAddress containerAddress;
}

public record RestartRemoteContainerOptions
{
    [SimpleOption("node", Description = "Node information", Required = true)]
    public string Node { get; init; } = string.Empty;

    [SimpleOption("remoteprivatekey", Description = "Private key for remote operation")]
    public string RemotePrivateKey { get; init; } = string.Empty;

    [SimpleOption("containerport", Description = "Port number associated with the container")]
    public ushort ContainerPort { get; init; } = NetConstants.MinPort;
}
