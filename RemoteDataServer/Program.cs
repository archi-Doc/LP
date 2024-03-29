﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1210 // Using directives should be ordered alphabetically by namespace

global using System.Net;
global using Arc.Crypto;
global using Arc.Threading;
global using Arc.Unit;
global using BigMachines;
global using Netsphere;
global using Tinyhand;
global using ValueLink;
using Microsoft.Extensions.DependencyInjection;
using SimpleCommandLine;

namespace RemoteDataServer;

public class Program
{
    public static async Task Main(string[] args)
    {
        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {// Console window closing or process terminated.
            ThreadCore.Root.Terminate(); // Send a termination signal to the root.
            ThreadCore.Root.TerminationEvent.WaitOne(2_000); // Wait until the termination process is complete (#1).
        };

        Console.CancelKeyPress += (s, e) =>
        {// Ctrl+C pressed
            e.Cancel = true;
            ThreadCore.Root.Terminate(); // Send a termination signal to the root.
        };

        var builder = new NetControl.Builder() // Create a NetControl builder.
            .Configure(context =>
            {
                context.AddSingleton<RemoteData>();
                context.AddTransient<RemoteDataBroker>();

                // Command
                context.AddCommand(typeof(DefaultCommand));

                context.AddLoggerResolver(context =>
                {// Logger
                    if (context.LogLevel == LogLevel.Debug)
                    {
                        context.SetOutput<EmptyLogger>();
                        return;
                    }

                    context.SetOutput<ConsoleLogger>();
                });
            })
            .SetupOptions<NetOptions>((context, options) =>
            {// Modify NetOptions
                options.NodeName = "RemoteDataServer";
                options.Port = 50000; // Specify the port number.
                options.EnableEssential = false; // Required when using functions such as Ping.
                options.EnableServer = true;
            })
            .ConfigureSerivice(context =>
            {// Register the services provided by the server.
                context.AddService<IRemoteData>();
            });

        var unit = builder.Build(); // Create a unit that provides network functionality.
        var options = unit.Context.ServiceProvider.GetRequiredService<NetOptions>();
        await unit.Run(options, false); // Execute the created unit with the specified options.

        // NtpCorrection
        var ntpCorrection = unit.Context.ServiceProvider.GetRequiredService<Netsphere.Misc.NtpCorrection>();
        var offset = await ntpCorrection.SendAndReceiveOffset();
        UnitLogger.SetTimeOffset(offset);
        await Console.Out.WriteLineAsync($"Corrected: {offset.ToString()}");

        await Console.Out.WriteLineAsync(options.ToString()); // Display the NetOptions.
        await Console.Out.WriteLineAsync();

        var parserOptions = SimpleParserOptions.Standard with
        {
            ServiceProvider = unit.Context.ServiceProvider,
            RequireStrictCommandName = false,
            RequireStrictOptionName = false,
        };

        await SimpleParser.ParseAndRunAsync(unit.Context.Commands, args, parserOptions); // Main process

        await unit.Terminate(); // Perform the termination process for the unit.
        ThreadCore.Root.Terminate();
        await ThreadCore.Root.WaitForTerminationAsync(-1); // Wait for the termination infinitely.
        ThreadCore.Root.TerminationEvent.Set(); // The termination process is complete (#1).
    }
}
