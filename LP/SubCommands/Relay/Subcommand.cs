﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace LP.Subcommands.Relay;

public static class Subcommand
{
    public static void Configure(IUnitConfigurationContext context)
    {
        context.AddSubcommand(typeof(ShowRelaySubcommand));
        context.AddSubcommand(typeof(ShowRelayExchangeSubcommand));
    }
}