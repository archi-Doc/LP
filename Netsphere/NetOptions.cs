﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Netsphere;

[TinyhandObject(ImplicitKeyAsName = true)]
public partial record NetOptions
{
    [SimpleOption("nodename", Description = "Node name")]
    public string NodeName { get; set; } = string.Empty;

    // [SimpleOption("address", Description = "Global IP address")]
    // public string Address { get; set; } = string.Empty;

    [SimpleOption("port", Description = "Port number associated with the address")]
    public int Port { get; set; }

    [SimpleOption("nodeprivatekey", Description = "Node private key")]
    public string NodePrivateKey { get; set; } = string.Empty;

    [SimpleOption("nodelist", Description = "Node addresses to connect")]
    public string NodeList { get; set; } = string.Empty;

    [SimpleOption("ping", Description = "Enable ping function")]
    public bool EnablePing { get; set; } = true;

    [SimpleOption("server", Description = "Enable server function")]
    public bool EnableServer { get; set; } = false;

    [SimpleOption("alternative", Description = "Enable alternative (debug) terminal")]
    public bool EnableAlternative { get; set; } = false;
}
