﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1210 // Using directives should be ordered alphabetically by namespace

global using System;
global using Arc.Crypto;
global using Arc.Threading;
global using Arc.Unit;
global using BigMachines;
global using CrystalData;
global using LP;
global using Tinyhand;
using LP.Crystal;
using LP.Data;
using LP.Logging;
using LP.Services;
using LP.T3CS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Netsphere;
using Netsphere.Logging;
using Netsphere.Machines;
using SimpleCommandLine;

namespace LP;

public class Control : ILogInformation
{
    public class Builder : UnitBuilder<Unit>
    {
        public Builder()
            : base()
        {
            this.Preload(context =>
            {
                this.LoadStrings();
                this.LoadLPOptions(context);
            });

            this.Configure(context =>
            {
                LPBase.Configure(context);

                // Main services
                context.AddSingleton<Control>();
                context.AddSingleton<LPBase>();
                context.Services.TryAddSingleton<IConsoleService, ConsoleUserInterfaceService>();
                context.Services.TryAddSingleton<IUserInterfaceService, ConsoleUserInterfaceService>();
                context.AddSingleton<Vault>();
                context.AddSingleton<IStorageKey, StorageKeyVault>();
                context.AddSingleton<Authority>();
                context.AddSingleton<Seedphrase>();
                // context.AddSingleton<Merger>();
                context.Services.TryAddSingleton<Merger.Provider>(x => x.GetRequiredService<Control>().MergerProvider);

                // Crystal
                context.AddSingleton<Mono>();
                context.AddSingleton<LpCrystal>();
                context.AddSingleton<MergerCrystal>();
                context.Services.TryAddSingleton<IBigCrystal>(x => x.GetRequiredService<Control>().Crystal);
                // context.Services.TryAddSingleton<LpData>(x => x.GetRequiredService<Control>().LpRoot);
                // context.Services.TryAddSingleton<MergerData>(x => x.GetRequiredService<Control>().MergerRoot);

                // RPC / Services
                context.AddSingleton<NetServices.AuthorizedTerminalFactory>();
                context.AddTransient<NetServices.BenchmarkServiceImpl>();
                context.AddSingleton<NetServices.RemoteBenchBroker>();
                context.AddTransient<NetServices.RemoteControlServiceImpl>();
                context.AddTransient<NetServices.T3CS.MergerServiceImpl>();

                // RPC / Filters
                context.AddTransient<NetServices.TestOnlyFilter>();
                context.AddTransient<NetServices.MergerOrTestFilter>();

                // Machines
                context.AddTransient<Machines.SingleMachine>();
                context.AddTransient<Machines.LogTesterMachine>();

                // Subcommands
                context.AddSubcommand(typeof(LP.Subcommands.TestSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.MicsSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.GCSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.PingSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.RemoteBenchSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.PunchSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.BenchmarkSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.SeedphraseSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.MergerSubcommand));

                LP.Subcommands.CrystalData.CrystalStorageSubcommand.Configure(context);
                LP.Subcommands.CrystalData.CrystalDataSubcommand.Configure(context);

                LP.Subcommands.TemplateSubcommand.Configure(context);
                LP.Subcommands.InfoSubcommand.Configure(context);
                LP.Subcommands.ExportSubcommand.Configure(context);
                LP.Subcommands.VaultSubcommand.Configure(context);
                LP.Subcommands.FlagSubcommand.Configure(context);
                LP.Subcommands.NodeSubcommand.Configure(context);
                LP.Subcommands.NodeKeySubcommand.Configure(context);
                LP.Subcommands.AuthoritySubcommand.Configure(context);
                LP.Subcommands.CustomSubcommand.Configure(context);
                LP.Subcommands.RemoteSubcommand.Configure(context);
                LP.Subcommands.MergerNestedcommand.Configure(context);
            });

            this.SetupOptions<FileLoggerOptions>((context, options) =>
            {// FileLoggerOptions
                var logfile = "Logs/Log.txt";
                if (context.TryGetOptions<LPOptions>(out var lpOptions))
                {
                    options.Path = Path.Combine(lpOptions.RootDirectory, logfile);
                }
                else
                {
                    options.Path = Path.Combine(context.RootDirectory, logfile);
                }

                options.MaxLogCapacity = 20;
            });

            this.SetupOptions<ClientTerminalLoggerOptions>((context, options) =>
            {// ClientTerminalLoggerOptions
                var logfile = "Logs/Client/.txt";
                if (context.TryGetOptions<LPOptions>(out var lpOptions))
                {
                    options.Path = Path.Combine(lpOptions.RootDirectory, logfile);
                }
                else
                {
                    options.Path = Path.Combine(context.RootDirectory, logfile);
                }

                options.MaxLogCapacity = 1;
            });

            this.SetupOptions<ServerTerminalLoggerOptions>((context, options) =>
            {// ServerTerminalLoggerOptions
                var logfile = "Logs/Server/.txt";
                if (context.TryGetOptions<LPOptions>(out var lpOptions))
                {
                    options.Path = Path.Combine(lpOptions.RootDirectory, logfile);
                }
                else
                {
                    options.Path = Path.Combine(context.RootDirectory, logfile);
                }

                options.MaxLogCapacity = 1;
            });

            this.SetupOptions<ConsoleLoggerOptions>((context, options) =>
            {// ConsoleLoggerOptions
                if (context.TryGetOptions<LPOptions>(out var lpOptions))
                {
                    options.Formatter.EnableColor = lpOptions.ColorConsole;
                }
                else
                {
                    options.Formatter.EnableColor = true;
                }
            });

            this.SetupOptions<LPBase>((context, lpBase) =>
            {// LPBase
                context.GetOptions<LPOptions>(out var options);
                lpBase.Initialize(options, true, "merger");
            });

            this.SetupOptions<NetBase>((context, netBase) =>
            {// NetBase
                context.GetOptions<LPOptions>(out var options);
                netBase.SetParameter(true, options.NodeName, options.NetsphereOptions);
                netBase.AllowUnsafeConnection = true; // betacode
            });

            this.SetupOptions<CrystalizerOptions>((context, options) =>
            {// CrystalizerOptions
                context.GetOptions<LPOptions>(out var lpOptions);
                options.RootPath = lpOptions.RootDirectory;
            });

            this.AddBuilder(new NetControl.Builder());
            this.AddBuilder(new CrystalControl.Builder());
            this.AddBuilder(new LP.Logging.LPLogger.Builder());
        }

        private void LoadStrings()
        {// Load strings
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            try
            {
                HashedString.LoadAssembly(null, asm, "Strings.strings-en.tinyhand");
                HashedString.LoadAssembly("ja", asm, "Strings.strings-en.tinyhand");
            }
            catch
            {
            }
        }

        private void LoadLPOptions(IUnitPreloadContext context)
        {
            var args = context.Arguments.RawArguments;
            LPOptions? options = null;

            if (context.Arguments.TryGetOption("loadoptions", out var optionFile))
            {// First - Option file
                if (!string.IsNullOrEmpty(optionFile))
                {
                    var originalPath = optionFile;
                    try
                    {
                        var utf8 = File.ReadAllBytes(originalPath);
                        var op = TinyhandSerializer.DeserializeFromUtf8<LPOptions>(utf8);
                        if (op != null)
                        {
                            options = op;
                            Console.WriteLine(HashedString.Get(Hashed.Success.Loaded, originalPath));
                        }
                    }
                    catch
                    {
                        Console.WriteLine(HashedString.Get(Hashed.Error.Load, originalPath));
                    }
                }
            }

            // Second - Arguments
            SimpleParser.TryParseOptions<LPOptions>(args, out options, options);

            if (options != null)
            {
                // Passphrase
                if (options.Pass == null)
                {
                    try
                    {
                        var lppass = Environment.GetEnvironmentVariable("lppass");
                        if (lppass != null)
                        {
                            options.Pass = lppass;
                        }
                    }
                    catch
                    {
                    }
                }

                context.SetOptions(options);
            }
        }
    }

    public class Unit : BuiltUnit
    {
        public Unit(UnitContext context)
            : base(context)
        {
            TinyhandSerializer.ServiceProvider = context.ServiceProvider;
        }

        public async Task RunAsync(LPOptions options)
        {
            var control = this.Context.ServiceProvider.GetRequiredService<Control>();
            try
            {
                // Settings
                await control.LoadSettingsAsync();

                // Vault
                await control.LoadVaultAsync();

                // Start
                control.Logger.Get<DefaultLog>().Log($"LP ({Version.Get()})");

                // Create optional instances
                this.Context.CreateInstances();

                // Prepare
                this.Context.SendPrepare(new());

                // Machines
                // control.NetControl.CreateMachines();
            }
            catch
            {
                control.Terminate(true);
                return;
            }

            try
            {// Load
                await control.LoadAsync(this.Context);
            }
            catch
            {
                await control.AbortAsync();
                control.Terminate(true);
                return;
            }

            try
            {// Start, Main loop
                await control.RunAsync(this.Context);

                await control.MainAsync();

                await control.TerminateAsync(this.Context);
                await control.SaveAsync(this.Context);
                control.Terminate(false);
            }
            catch
            {
                await control.TerminateAsync(this.Context);
                await control.SaveAsync(this.Context);
                control.Terminate(true);
                return;
            }
        }
    }

    public Control(UnitContext context, UnitCore core, UnitLogger logger, IUserInterfaceService userInterfaceService, LPBase lpBase, BigMachine<Identifier> bigMachine, NetControl netsphere, Mono mono, Vault vault, Authority authority)
    {
        this.Logger = logger;
        this.UserInterfaceService = userInterfaceService;
        this.LPBase = lpBase;
        this.BigMachine = bigMachine; // Warning: Can't call BigMachine.TryCreate() in a constructor.
        this.NetControl = netsphere;
        this.NetControl.SetupServer(() => new NetServices.LPServerContext(), () => new NetServices.LPCallContext());
        this.Mono = mono;
        this.Vault = vault;
        this.Authority = authority;

        this.MergerProvider = new();
        if (this.LPBase.Mode == LPMode.Merger)
        {// Merger
            var mergerCrystal = context.ServiceProvider.GetRequiredService<MergerCrystal>();
            this.Crystal = mergerCrystal;
            this.LpRoot = default!;
            this.MergerRoot = mergerCrystal.Object;
            this.MergerProvider.Create(context);
        }
        else
        {
            var lpCrystal = context.ServiceProvider.GetRequiredService<LpCrystal>();
            this.Crystal = lpCrystal;
            this.LpRoot = lpCrystal.Object.Data;
            this.MergerRoot = default!;
        }

        this.Core = core;
        this.BigMachine.Core.ChangeParent(this.Core);

        SubcommandParserOptions = SimpleParserOptions.Standard with
        {
            ServiceProvider = context.ServiceProvider,
            RequireStrictCommandName = true,
            RequireStrictOptionName = true,
            DoNotDisplayUsage = true,
            DisplayCommandListAsHelp = true,
        };

        this.subcommandParser = new SimpleParser(context.Subcommands, SubcommandParserOptions);
    }

    public async Task LoadAsync(UnitContext context)
    {
        // Netsphere
        await this.NetControl.EssentialNode.LoadAsync(Path.Combine(this.LPBase.DataDirectory, EssentialNode.FileName)).ConfigureAwait(false);

        // CrystalData
        if (!await this.Mono.LoadAsync(Path.Combine(this.LPBase.DataDirectory, Mono.DefaultMonoFile)).ConfigureAwait(false))
        {
            await this.Mono.LoadAsync(Path.Combine(this.LPBase.DataDirectory, Mono.DefaultMonoBackup)).ConfigureAwait(false);
        }

        var result = await this.Crystal.PrepareAndLoad(new());
        if (result != CrystalStartResult.Success)
        {
            throw new PanicException();
        }

        await context.SendLoadAsync(new(this.LPBase.DataDirectory));
    }

    public async Task AbortAsync()
    {
        // await this.Crystal.Abort(); // tempcode
    }

    public async Task SaveAsync(UnitContext context)
    {
        Directory.CreateDirectory(this.LPBase.DataDirectory);

        await this.SaveSettingsAsync();
        await this.SaveKeyVaultAsync();
        await this.NetControl.EssentialNode.SaveAsync(Path.Combine(this.LPBase.DataDirectory, EssentialNode.FileName)).ConfigureAwait(false);
        await this.Mono.SaveAsync(Path.Combine(this.LPBase.DataDirectory, Mono.DefaultMonoFile), Path.Combine(this.LPBase.DataDirectory, Mono.DefaultMonoBackup));

        // await this.Crystal.StopAsync(new()); // tempcode
        await this.Crystal.Save();

        await context.SendSaveAsync(new(this.LPBase.DataDirectory));
    }

    public async Task RunAsync(UnitContext context)
    {
        this.BigMachine.Start();

        // this.BigMachine.CreateOrGet<EssentialNetMachine.Interface>(Identifier.Zero)?.RunAsync();
        this.BigMachine.CreateOrGet<NtpMachine.Interface>(Identifier.Zero)?.RunAsync();
        this.BigMachine.CreateOrGet<PublicIPMachine.Interface>(Identifier.Zero)?.RunAsync();

        await context.SendRunAsync(new(this.Core));

        this.UserInterfaceService.WriteLine();
        var logger = this.Logger.Get<DefaultLog>(LogLevel.Information);
        this.LogInformation(logger);

        logger.Log("Press Enter key to switch to console mode.");
        logger.Log("Press Ctrl+C to exit.");
        logger.Log("Running");
    }

    public void LogInformation(ILog logger)
    {
        logger.Log($"Utc: {Mics.ToString(Mics.GetUtcNow())}");
        this.LPBase.LogInformation(logger);
    }

    public async Task TerminateAsync(UnitContext context)
    {
        this.Logger.Get<DefaultLog>().Log("Termination process initiated");

        try
        {
            await context.SendTerminateAsync(new());
        }
        catch
        {
        }
    }

    public void Terminate(bool abort)
    {
        this.Core.Terminate();
        this.Core.WaitForTermination(-1);

        this.Logger.Get<DefaultLog>().Log(abort ? "Aborted" : "Terminated");
        this.Logger.FlushAndTerminate().Wait(); // Write logs added after Terminate().
    }

    public async Task<bool> TryTerminate()
    {
        if (!this.LPBase.Options.ConfirmExit)
        {// No confirmation
            this.Core.Terminate(); // this.Terminate(false);
            return true;
        }

        var result = await this.UserInterfaceService.RequestYesOrNo(Hashed.Dialog.ConfirmExit);
        if (result == true)
        {
            this.Core.Terminate(); // this.Terminate(false);
            return true;
        }

        return false;
    }

    public bool Subcommand(string subcommand)
    {
        if (!this.subcommandParser.Parse(subcommand))
        {
            if (this.subcommandParser.HelpCommand != string.Empty)
            {
                this.subcommandParser.ShowHelp();
                return true;
            }
            else
            {
                this.UserInterfaceService.WriteLine("Invalid subcommand.");
                return false;
            }
        }

        this.subcommandParser.Run();
        return true;

        /*if (subcommandParser.HelpCommand != string.Empty)
        {
            return false;
        }

        this.ConsoleService.WriteLine();
        return true;*/
    }

    private async Task MainAsync()
    {
        while (!this.Core.IsTerminated)
        {
            var currentMode = this.UserInterfaceService.CurrentMode;
            if (currentMode == IUserInterfaceService.Mode.Console)
            {// Console mode
                string? command = null;
                try
                {
                    command = await Task.Run(() =>
                    {
                        return this.UserInterfaceService.ReadLine()?.Trim();
                    }).WaitAsync(this.Core.CancellationToken).ConfigureAwait(false);
                }
                catch
                {
                }

                // var command = this.UserInterfaceService.ReadLine()?.Trim();
                if (!string.IsNullOrEmpty(command))
                {
                    if (string.Compare(command, "exit", true) == 0)
                    {// Exit
                        if (this.TryTerminate().Result == true)
                        { // To view mode
                            this.UserInterfaceService.ChangeMode(IUserInterfaceService.Mode.View);
                            return;
                        }
                        else
                        {
                            this.UserInterfaceService.Write("> ");
                            continue;
                        }
                    }
                    else
                    {// Subcommand
                        try
                        {
                            this.Subcommand(command);
                            this.UserInterfaceService.Write("> ");
                            continue;
                        }
                        catch (Exception e)
                        {
                            this.UserInterfaceService.WriteLine(e.ToString());
                            break;
                        }
                    }
                }
                else
                {
                    this.UserInterfaceService.WriteLine();
                }

                // To view mode
                this.UserInterfaceService.ChangeMode(IUserInterfaceService.Mode.View);
            }
            else if (currentMode == IUserInterfaceService.Mode.View)
            {// View mode
                if (this.UserInterfaceService.KeyAvailable)
                {
                    var keyInfo = this.UserInterfaceService.ReadKey(true);
                    if (keyInfo.Key == ConsoleKey.Enter || keyInfo.Key == ConsoleKey.Escape)
                    { // To console mode
                        this.UserInterfaceService.ChangeMode(IUserInterfaceService.Mode.Console);
                        this.UserInterfaceService.Write("> ");
                    }
                    else
                    {
                        while (this.UserInterfaceService.KeyAvailable)
                        {
                            this.UserInterfaceService.ReadKey(true);
                        }
                    }
                }
            }

            this.Core.Sleep(100, 100);
        }

        // To view mode
        this.UserInterfaceService.ChangeMode(IUserInterfaceService.Mode.View);
    }

    public static SimpleParserOptions SubcommandParserOptions { get; private set; } = default!;

    public UnitLogger Logger { get; }

    public UnitCore Core { get; }

    public IUserInterfaceService UserInterfaceService { get; }

    public LPBase LPBase { get; }

    public BigMachine<Identifier> BigMachine { get; }

    public NetControl NetControl { get; }

    public Merger.Provider MergerProvider { get; }

    public IBigCrystal Crystal { get; }

    public LpData LpRoot { get; }

    public MergerData MergerRoot { get; }

    public Mono Mono { get; }

    public Vault Vault { get; }

    public Authority Authority { get; }

    private SimpleParser subcommandParser;

    private async Task LoadVaultAsync()
    {
        if (this.LPBase.IsFirstRun)
        {// First run
        }
        else
        {
            var result = await this.Vault.LoadAsync(this.LPBase.CombineDataPath(this.LPBase.Options.Vault, Vault.Filename), this.LPBase.Options.Pass).ConfigureAwait(false);
            if (result)
            {
                goto LoadKeyVaultObjects;
            }

            // Could not load Vault
            var reply = await this.UserInterfaceService.RequestYesOrNo(Hashed.Vault.AskNew);
            if (reply != true)
            {// No
                throw new PanicException();
            }
        }

        this.UserInterfaceService.WriteLine(HashedString.Get(Hashed.Vault.Create));
        // await this.UserInterfaceService.Notify(UserInterfaceNotifyLevel.Information, Hashed.KeyVault.Create);

        // New Vault
        var password = this.LPBase.Options.Pass;
        if (string.IsNullOrEmpty(password))
        {
            password = await this.UserInterfaceService.RequestPasswordAndConfirm(Hashed.Vault.EnterPassword, Hashed.Dialog.Password.Confirm);
        }

        if (password == null)
        {
            throw new PanicException();
        }

        this.Vault.Create(password);

LoadKeyVaultObjects:
        await this.LoadKeyVault_NodeKey();
    }

    private async Task LoadKeyVault_NodeKey()
    {
        if (!this.Vault.TryGetAndDeserialize<NodePrivateKey>(NodePrivateKey.PrivateKeyPath, out var key))
        {// Failure
            if (!this.Vault.Created)
            {
                await this.UserInterfaceService.Notify(LogLevel.Error, Hashed.Vault.NoData, NodePrivateKey.PrivateKeyPath);
            }

            return;
        }

        if (!this.NetControl.NetBase.SetNodeKey(key))
        {
            await this.UserInterfaceService.Notify(LogLevel.Error, Hashed.Vault.NoRestore, NodePrivateKey.PrivateKeyPath);
            return;
        }
    }

    private async Task SaveKeyVaultAsync()
    {
        this.Vault.Add(NodePrivateKey.PrivateKeyPath, this.NetControl.NetBase.SerializeNodeKey());

        await this.Vault.SaveAsync(this.LPBase.CombineDataPath(this.LPBase.Options.Vault, Vault.Filename));
    }

    private async Task LoadSettingsAsync()
    {
        if (this.LPBase.IsFirstRun)
        {
            return;
        }

        var path = Path.Combine(this.LPBase.DataDirectory, LPSettings.DefaultSettingsName);
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch
        {
            return;
        }

        LPSettings? settings;
        try
        {
            settings = TinyhandSerializer.DeserializeFromUtf8<LPSettings>(data);
            if (settings != null)
            {
                this.LPBase.Settings = settings;
            }
        }
        catch
        {
            this.Logger.Get<DefaultLog>(LogLevel.Error).Log(Hashed.Error.Deserialize, path);
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var path = Path.Combine(this.LPBase.DataDirectory, LPSettings.DefaultSettingsName);
            var bytes = TinyhandSerializer.SerializeToUtf8(this.LPBase.Settings);
            await File.WriteAllBytesAsync(path, bytes);
        }
        catch
        {
        }
    }
}
