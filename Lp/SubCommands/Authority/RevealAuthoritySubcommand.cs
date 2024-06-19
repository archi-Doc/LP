﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Lp.T3cs;
using SimpleCommandLine;

namespace Lp.Subcommands;

[SimpleCommand("reveal-authority")]
public class RevealAuthoritySubcommand : ISimpleCommandAsync<AuthoritySubcommandNameOptions>
{
    public RevealAuthoritySubcommand(IConsoleService consoleService, ILogger<RevealAuthoritySubcommand> logger, AuthorityVault authorityVault)
    {
        this.consoleService = consoleService;
        this.logger = logger;
        this.authorityVault = authorityVault;
    }

    public async Task RunAsync(AuthoritySubcommandNameOptions option, string[] args)
    {
        var authority = await this.authorityVault.GetAuthority(option.AuthorityName);
        if (authority != null)
        {
            this.consoleService.WriteLine($"{option.AuthorityName}: {authority.UnsafeToString()}");
        }
        else
        {
            this.logger.TryGet(LogLevel.Warning)?.Log(Hashed.Authority.NotAvailable, option.AuthorityName);
        }
    }

    private readonly IConsoleService consoleService;
    private readonly ILogger logger;
    private readonly AuthorityVault authorityVault;
}
