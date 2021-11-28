﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1210 // Using directives should be ordered alphabetically by namespace
global using System;
global using System.IO;
global using System.Threading.Tasks;
global using Arc.Threading;
global using BigMachines;
global using CrossChannel;
global using LP;
using DryIoc;
using LP.Net;
using SimpleCommandLine;

namespace NetsphereTest;

public class NetControl
{
    public static void Register(Container container)
    {
        // Container instance
        containerInstance = container;

        // Base
        container.RegisterDelegate(x => new BigMachine<Identifier>(container), Reuse.Singleton);

        // Main services
        container.Register<NetControl>(Reuse.Singleton);
        container.Register<Information>(Reuse.Singleton);
        container.Register<Private>(Reuse.Singleton);
        container.Register<Netsphere>(Reuse.Singleton);
        container.Register<Terminal>(Reuse.Singleton);
        container.Register<EssentialNode>(Reuse.Singleton);
        container.Register<NetStatus>(Reuse.Singleton);
        container.Register<Server>(Reuse.Transient);

        // Machines
        container.Register<LP.Machines.EssentialNetMachine>();
    }

    public NetControl(Information information, Private @private, BigMachine<Identifier> bigMachine, Netsphere netsphere)
    {
        this.Information = information;
        this.Private = @private;
        this.BigMachine = bigMachine; // Warning: Can't call BigMachine.TryCreate() in a constructor.
        this.Netsphere = netsphere;
        this.Netsphere.SetServerTerminalDelegate(CreateServerTerminal);

        this.Core = new(ThreadCore.Root);
        this.BigMachine.Core.ChangeParent(this.Core);
    }

    public void Configure()
    {
        Logger.Configure(this.Information);
        this.ConfigureControl();

        Radio.Send(new Message.Configure());
    }

    public async Task LoadAsync()
    {
        await Radio.SendAsync(new Message.LoadAsync());
    }

    public async Task SaveAsync()
    {
        await Radio.SendAsync(new Message.SaveAsync());
    }

    public void ConfigureControl()
    {
        if (this.Private.NodePrivateKey == null)
        {
            this.Private.NodePrivateKey = NodePrivateKey.Create();
            this.Private.NodePrivateEcdh = NodeKey.FromPrivateKey(this.Private.NodePrivateKey)!;
            this.Information.NodePublicKey = new NodePublicKey(this.Private.NodePrivateKey);
            this.Information.NodePublicEcdh = NodeKey.FromPublicKey(this.Information.NodePublicKey.X, this.Information.NodePublicKey.Y)!;
        }
    }

    public bool TryStart()
    {
        var s = this.Information.IsConsole ? " (Console)" : string.Empty;
        Logger.Default.Information("LP Start" + s);

        Logger.Default.Information($"Console: {this.Information.IsConsole}, Root directory: {this.Information.RootDirectory}");
        Logger.Default.Information(this.Information.ToString());
        Logger.Console.Information("Press the Enter key to change to console mode.");
        Logger.Console.Information("Press Ctrl+C to exit.");

        var message = new Message.Start(this.Core);
        Radio.Send(message);
        if (message.Abort)
        {
            Radio.Send(new Message.Stop());
            return false;
        }

        this.BigMachine.Start();

        return true;
    }

    public void Stop()
    {
        Logger.Default.Information("LP Termination process initiated");

        Radio.Send(new Message.Stop());
    }

    public void Terminate()
    {
        this.Core.Terminate();
        this.Core.WaitForTermination(-1);

        Logger.Default.Information("LP Teminated");
        Logger.CloseAndFlush();
    }

    public ThreadCoreGroup Core { get; }

    public Information Information { get; }

    public Private Private { get; }

    public BigMachine<Identifier> BigMachine { get; }

    public Netsphere Netsphere { get; }

    private static Container containerInstance = default!;

    private static void CreateServerTerminal(NetTerminalServer terminal)
    {
        Task.Run(() =>
        {
            var server = containerInstance.Resolve<Server>();
            try
            {
                server.Process(terminal);
            }
            finally
            {
                server.Core?.Sleep(1000);
                terminal.Dispose();
            }
        });
    }

    private void Dump()
    {
        Logger.Default.Information($"Dump:");
        Logger.Default.Information($"MyStatus: {this.Netsphere.MyStatus.Type}");
    }
}
