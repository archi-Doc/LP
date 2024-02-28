﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using Arc.Threading;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;

namespace QuickStart;

public class Program
{
    public static async Task Main(string[] args)
    {
        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {// Console window closing or process terminated.
            ThreadCore.Root.Terminate(); // Send a termination signal to the root.
            ThreadCore.Root.TerminationEvent.WaitOne(2000); // Wait until the termination process is complete (#1).
        };

        Console.CancelKeyPress += (s, e) =>
        {// Ctrl+C pressed
            e.Cancel = true;
            ThreadCore.Root.Terminate(); // Send a termination signal to the root.
        };

        var builder = new NetControl.Builder() // Create a NetControl builder.
            .SetupOptions<NetOptions>((context, options) =>
            {// Modify NetOptions.
                options.NodeName = "Test server";
                options.Port = 49152; // Specify the port number.
                options.EnableEssential = true; // Required when using functions such as Ping.
                options.EnableServer = true;
            })
            .ConfigureSerivice(context =>
            {// Register the services provided by the server.
                context.AddService<ITestService>();
            });

        var unit = builder.Build(); // Create a unit that provides network functionality.
        var options = unit.Context.ServiceProvider.GetRequiredService<NetOptions>();
        await Console.Out.WriteLineAsync(options.ToString()); // Display the NetOptions.

        await unit.Run(options, true); // Execute the created unit with the specified options.

        var netBase = unit.Context.ServiceProvider.GetRequiredService<NetBase>();
        var node = new NetNode(new(IPAddress.Loopback, (ushort)options.Port), netBase.NodePublicKey);

        await Console.Out.WriteLineAsync($"Server: {node.ToString()}");
        await Console.Out.WriteLineAsync("Ctrl+C to exit");
        await ThreadCore.Root.Delay(100_000); // Wait until the server shuts down.
        await unit.Terminate(); // Perform the termination process for the unit.

        ThreadCore.Root.Terminate();
        await ThreadCore.Root.WaitForTerminationAsync(-1); // Wait for the termination infinitely.
        ThreadCore.Root.TerminationEvent.Set(); // The termination process is complete (#1).
    }
}
