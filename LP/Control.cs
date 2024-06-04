﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1210 // Using directives should be ordered alphabetically by namespace

global using System;
global using Arc.Crypto;
global using Arc.Threading;
global using Arc.Unit;
global using BigMachines;
global using CrystalData;
global using LP;
global using Netsphere;
global using Tinyhand;
global using ValueLink;
using LP.Data;
using LP.NetServices;
using LP.Services;
using LP.T3CS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Netsphere.Crypto;
using Netsphere.Relay;
using SimpleCommandLine;

namespace LP;

public class Control
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
                // Base
                LpBase.Configure(context);

                // Main services
                context.AddSingleton<Control>();
                context.AddSingleton<LpBase>();
                context.Services.TryAddSingleton<IConsoleService, ConsoleUserInterfaceService>();
                context.Services.TryAddSingleton<IUserInterfaceService, ConsoleUserInterfaceService>();
                context.AddSingleton<Vault>();
                context.AddSingleton<IStorageKey, StorageKeyVault>();
                context.AddSingleton<AuthorityVault>();
                context.AddSingleton<Seedphrase>();
                context.AddSingleton<Merger>();
                context.AddSingleton<RelayMerger>();
                ConfigureRelay(context);

                // RPC / Services
                context.AddSingleton<NetServices.AuthenticatedTerminalFactory>();
                context.AddSingleton<NetServices.RemoteBenchControl>();
                context.AddSingleton<NetServices.RemoteBenchHostAgent>();
                context.AddTransient<LP.T3CS.MergerServiceAgent>();

                // RPC / Filters
                context.AddTransient<NetServices.TestOnlyFilter>();
                context.AddTransient<NetServices.MergerOrTestFilter>();

                // Machines
                context.AddSingleton<BigMachine>();
                context.AddSingleton<BigMachineBase, BigMachine>();
                context.AddTransient<Machines.TemplateMachine>();
                context.AddTransient<Machines.LogTesterMachine>();
                context.AddTransient<Machines.LpControlMachine>();
                context.AddSingleton<Machines.RelayPeerMachine>();

                // Subcommands
                context.AddSubcommand(typeof(LP.Subcommands.TestSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.MicsSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.GCSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.PingSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.RestartRemoteContainerSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.RemoteBenchSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.RemoteDataSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.PunchSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.BenchmarkSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.SeedphraseSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.MergerSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.NewTokenSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.RevealAuthoritySubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.NewSignatureKeySubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.ShowOwnNodeSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.GetNetNodeSubcommand));

                // Vault
                context.AddSubcommand(typeof(LP.Subcommands.NewVaultSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.RemoveVaultSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.ListVaultSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.ShowVaultSubcommand));
                context.AddSubcommand(typeof(LP.Subcommands.ChangeVaultPassSubcommand));

                // LP.Subcommands.CrystalData.CrystalStorageSubcommand.Configure(context);
                // LP.Subcommands.CrystalData.CrystalDataSubcommand.Configure(context);

                LP.Subcommands.InfoSubcommand.Configure(context);
                LP.Subcommands.ExportSubcommand.Configure(context);
                LP.Subcommands.FlagSubcommand.Configure(context);
                LP.Subcommands.NodeSubcommand.Configure(context);
                LP.Subcommands.NodeKeySubcommand.Configure(context);
                LP.Subcommands.AuthoritySubcommand.Configure(context);
                LP.Subcommands.CustomSubcommand.Configure(context);
                LP.Subcommands.MergerNestedcommand.Configure(context);
                LP.Subcommands.Relay.Subcommand.Configure(context);
            });

            this.SetupOptions<FileLoggerOptions>((context, options) =>
            {// FileLoggerOptions
                var logfile = "Logs/Log.txt";
                if (context.TryGetOptions<LpOptions>(out var lpOptions))
                {
                    options.Path = Path.Combine(lpOptions.RootDirectory, logfile);
                }
                else
                {
                    options.Path = Path.Combine(context.RootDirectory, logfile);
                }

                options.MaxLogCapacity = 20;
            });

            this.SetupOptions<LP.Logging.NetsphereLoggerOptions>((context, options) =>
            {// NetsphereLoggerOptions, LogLowLevelNet
                var logfile = "Logs/Net.txt";
                if (context.TryGetOptions<LpOptions>(out var lpOptions))
                {
                    options.Path = Path.Combine(lpOptions.RootDirectory, logfile);
                }
                else
                {
                    options.Path = Path.Combine(context.RootDirectory, logfile);
                }

                options.MaxLogCapacity = 100;
                options.Formatter.TimestampFormat = "mm:ss.ffffff K";
                options.ClearLogsAtStartup = true;
                options.MaxQueue = 100_000;
            });

            this.SetupOptions<ConsoleLoggerOptions>((context, options) =>
            {// ConsoleLoggerOptions
                if (context.TryGetOptions<LpOptions>(out var lpOptions))
                {
                    options.Formatter.EnableColor = lpOptions.ColorConsole;
                }
                else
                {
                    options.Formatter.EnableColor = true;
                }
            });

            this.SetupOptions<LpBase>((context, lpBase) =>
            {// LPBase
                context.GetOptions<LpOptions>(out var options);
                lpBase.Initialize(options, true, "merger");
            });

            this.SetupOptions<NetBase>((context, netBase) =>
            {// NetBase
                context.GetOptions<LpOptions>(out var options);
                netBase.SetOptions(options.ToNetOptions());

                netBase.AllowUnsafeConnection = true; // betacode
                netBase.NetOptions.EnableServer = true; // betacode
                netBase.DefaultAgreement = netBase.DefaultAgreement with { MaxStreamLength = 100_000_000, }; // betacode
            });

            this.SetupOptions<CrystalizerOptions>((context, options) =>
            {// CrystalizerOptions
                context.GetOptions<LpOptions>(out var lpOptions);
                // options.RootPath = lpOptions.RootDirectory;
                options.DefaultSaveFormat = SaveFormat.Utf8;
                options.DefaultSavePolicy = SavePolicy.Periodic;
                options.DefaultSaveInterval = TimeSpan.FromMinutes(10);
                options.GlobalDirectory = new LocalDirectoryConfiguration(LpBase.DataDirectoryName);
                options.EnableFilerLogger = false;
            });

            var crystalControlBuilder = CrystalBuilder();

            this.AddBuilder(new NetControl.Builder());
            this.AddBuilder(crystalControlBuilder);
            this.AddBuilder(new LP.Logging.LPLogger.Builder());
        }

        private static void ConfigureRelay(IUnitConfigurationContext context)
        {
            if (context.TryGetOptions<LpOptions>(out var options))
            {
                if (SignaturePublicKey.TryParse(options.CertificateRelayPublicKey, out var relayPublicKey))
                {// CertificateRelayControl
                    context.AddSingleton<IRelayControl, CertificateRelayControl>();
                }
            }
        }

        private static CrystalControl.Builder CrystalBuilder()
        {
            return new CrystalControl.Builder()
                .ConfigureCrystal(context =>
                {
                    context.AddCrystal<LPSettings>(new()
                    {
                        NumberOfFileHistories = 0,
                        FileConfiguration = new GlobalFileConfiguration(LPSettings.Filename),
                        RequiredForLoading = true,
                    });

                    context.AddCrystal<Mono>(new()
                    {
                        SaveFormat = SaveFormat.Binary,
                        NumberOfFileHistories = 0,
                        FileConfiguration = new GlobalFileConfiguration("Mono"),
                    });

                    context.AddCrystal<Netsphere.Stats.NetStats>(new CrystalConfiguration() with
                    {
                        NumberOfFileHistories = 2,
                        FileConfiguration = new GlobalFileConfiguration("NetStat.tinyhand"),
                    });

                    context.AddCrystal<Netsphere.Misc.NtpCorrection>(new CrystalConfiguration() with
                    {
                        NumberOfFileHistories = 0,
                        FileConfiguration = new GlobalFileConfiguration("NtpCorrection.tinyhand"),
                    });
                });
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
            LpOptions? options = null;

            if (context.Arguments.TryGetOption("loadoptions", out var optionFile))
            {// 1st: Option file
                if (!string.IsNullOrEmpty(optionFile))
                {
                    var originalPath = optionFile;
                    try
                    {
                        var utf8 = File.ReadAllBytes(originalPath);
                        var op = TinyhandSerializer.DeserializeFromUtf8<LpOptions>(utf8);
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

            // 2nd: Arguments
            SimpleParser.TryParseOptions<LpOptions>(args, out options, options);

            if (options is not null)
            {
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

        public async Task RunAsync(LpOptions options)
        {
            try
            {
                // Crystalizer
                var crystalizer = this.Context.ServiceProvider.GetRequiredService<Crystalizer>();

                // Vault
                var vault = this.Context.ServiceProvider.GetRequiredService<Vault>();
                await vault.LoadAsync();
                ((StorageKeyVault)this.Context.ServiceProvider.GetRequiredService<IStorageKey>()).Vault = vault;

                // Load
                var result = await crystalizer.PrepareAndLoadAll();
                if (result != CrystalResult.Success)
                {
                    throw new PanicException();
                }
            }
            catch
            {
                ThreadCore.Root.Terminate();
                return;
            }

            var control = this.Context.ServiceProvider.GetRequiredService<Control>();
            try
            {
                // Start
                control.Logger.Get<DefaultLog>().Log($"LP ({Netsphere.Version.VersionString})");

                // Merger, Relay, Peer
                await control.CreateMerger(this.Context);
                await control.CreatePeer(this.Context);
                await control.CreateRelay(this.Context);

                // Vault -> NodeKey
                await control.LoadKeyVault_NodeKey();

                // Create optional instances
                this.Context.CreateInstances();

                // Prepare
                this.Context.SendPrepare(new());
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
                await control.StartAsync(this.Context);

                await control.MainAsync();

                this.Context.SendStop(new());
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

    public Control(UnitContext context, UnitCore core, UnitLogger logger, IUserInterfaceService userInterfaceService, LpBase lpBase, BigMachine bigMachine, NetControl netsphere, Crystalizer crystalizer, Vault vault, AuthorityVault authorityVault, LPSettings settings, Merger merger, RelayMerger relayMerger)
    {
        this.Logger = logger;
        this.UserInterfaceService = userInterfaceService;
        this.LPBase = lpBase;
        this.BigMachine = bigMachine; // Warning: Can't call BigMachine.TryCreate() in a constructor.
        this.NetControl = netsphere;
        this.Crystalizer = crystalizer;
        this.Vault = vault;
        this.AuthorityVault = authorityVault;
        this.LPBase.Settings = settings;
        this.Merger = merger;
        this.RelayMerger = relayMerger;

        if (this.LPBase.Options.TestFeatures)
        {
            NetAddress.SkipValidation = true;
            this.NetControl.Services.Register<IRemoteBenchHost>();
        }

        this.Core = core;

        SubcommandParserOptions = SimpleParserOptions.Standard with
        {
            ServiceProvider = context.ServiceProvider,
            RequireStrictCommandName = true,
            RequireStrictOptionName = true,
            DoNotDisplayUsage = true,
            DisplayCommandListAsHelp = true,
            AutoAlias = true,
        };

        this.subcommandParser = new SimpleParser(context.Subcommands, SubcommandParserOptions);
    }

    public static SimpleParserOptions SubcommandParserOptions { get; private set; } = default!;

    public UnitLogger Logger { get; }

    public UnitCore Core { get; }

    public IUserInterfaceService UserInterfaceService { get; }

    public LpBase LPBase { get; }

    public BigMachine BigMachine { get; }

    public NetControl NetControl { get; }

    public Merger Merger { get; }

    public RelayMerger RelayMerger { get; }

    public Crystalizer Crystalizer { get; }

    public Vault Vault { get; }

    public AuthorityVault AuthorityVault { get; }

    private SimpleParser subcommandParser;

    public async Task CreatePeer(UnitContext context)
    {
        if (!string.IsNullOrEmpty(this.LPBase.Options.RelayPeerPrivault))
        {// RelayPeerPrivault is valid
            var privault = this.LPBase.Options.RelayPeerPrivault;
            if (!SignaturePrivateKey.TryParse(privault, out var privateKey))
            {// 1st: Tries to parse as SignaturePrivateKey, 2nd : Tries to get from Vault.
                if (!this.Vault.TryGetAndDeserialize<SignaturePrivateKey>(privault, out privateKey))
                {
                    await this.UserInterfaceService.Notify(LogLevel.Error, Hashed.Merger.NoPrivateKey, privault);
                    privateKey = SignaturePrivateKey.Create();
                    this.Vault.SerializeAndTryAdd(privault, privateKey);
                }
            }
        }

        if (!string.IsNullOrEmpty(this.LPBase.Options.ContentPeerPrivault))
        {// ContentPeerPrivault is valid
            var privault = this.LPBase.Options.ContentPeerPrivault;
            if (!SignaturePrivateKey.TryParse(privault, out var privateKey))
            {// 1st: Tries to parse as SignaturePrivateKey, 2nd : Tries to get from Vault.
                if (!this.Vault.TryGetAndDeserialize<SignaturePrivateKey>(privault, out privateKey))
                {
                    await this.UserInterfaceService.Notify(LogLevel.Error, Hashed.Merger.NoPrivateKey, privault);
                    privateKey = SignaturePrivateKey.Create();
                    this.Vault.SerializeAndTryAdd(privault, privateKey);
                }
            }
        }
    }

    public async Task CreateRelay(UnitContext context)
    {
        if (context.ServiceProvider.GetService<IRelayControl>() is CertificateRelayControl certificateRelayControl)
        {
            if (SignaturePublicKey.TryParse(this.LPBase.Options.CertificateRelayPublicKey, out var relayPublicKey))
            {
                certificateRelayControl.SetCertificatePublicKey(relayPublicKey);
                this.Logger.Get<CertificateRelayControl>().Log($"{relayPublicKey.ToString()}");
            }
        }
    }

    public async Task CreateMerger(UnitContext context)
    {
        var crystalizer = context.ServiceProvider.GetRequiredService<Crystalizer>();
        if (!string.IsNullOrEmpty(this.LPBase.Options.CreditMergerPrivault))
        {// CreditMergerPrivault is valid
            var privault = this.LPBase.Options.CreditMergerPrivault;
            if (!SignaturePrivateKey.TryParse(privault, out var privateKey))
            {// 1st: Tries to parse as SignaturePrivateKey, 2nd : Tries to get from Vault.
                if (!this.Vault.TryGetAndDeserialize<SignaturePrivateKey>(privault, out privateKey))
                {
                    await this.UserInterfaceService.Notify(LogLevel.Error, Hashed.Merger.NoPrivateKey, privault);
                    privateKey = SignaturePrivateKey.Create();
                    this.Vault.SerializeAndTryAdd(privault, privateKey);
                }
            }

            context.ServiceProvider.GetRequiredService<Merger>().Initialize(crystalizer, privateKey);
            this.NetControl.Services.Register<IMergerService>();
        }

        if (!string.IsNullOrEmpty(this.LPBase.Options.RelayMergerPrivault))
        {// RelayMergerPrivault is valid
            var privault = this.LPBase.Options.RelayMergerPrivault;
            if (!SignaturePrivateKey.TryParse(privault, out var privateKey))
            {// 1st: Tries to parse as SignaturePrivateKey, 2nd : Tries to get from Vault.
                if (!this.Vault.TryGetAndDeserialize<SignaturePrivateKey>(privault, out privateKey))
                {
                    await this.UserInterfaceService.Notify(LogLevel.Error, Hashed.Merger.NoPrivateKey, privault);
                    privateKey = SignaturePrivateKey.Create();
                    this.Vault.SerializeAndTryAdd(privault, privateKey);
                }
            }

            context.ServiceProvider.GetRequiredService<RelayMerger>().Initialize(crystalizer, privateKey);
            this.NetControl.Services.Register<IRelayMergerService>();
        }
    }

    public async Task LoadAsync(UnitContext context)
    {
        await context.SendLoadAsync(new(this.LPBase.DataDirectory));
    }

    public async Task AbortAsync()
    {
        // await this.Crystalizer.SaveAllAndTerminate();
    }

    public async Task SaveAsync(UnitContext context)
    {
        Directory.CreateDirectory(this.LPBase.DataDirectory);

        // Vault
        this.Vault.Add(NetConstants.NodePrivateKeyName, this.NetControl.NetBase.SerializeNodePrivateKey());
        await this.Vault.SaveAsync();

        await context.SendSaveAsync(new(this.LPBase.DataDirectory));

        await this.Crystalizer.SaveAllAndTerminate();
    }

    public async Task StartAsync(UnitContext context)
    {
        this.BigMachine.Start(null);
        this.RunMachines();

        await context.SendStartAsync(new(this.Core));

        this.UserInterfaceService.WriteLine();
        var logger = this.Logger.Get<DefaultLog>(LogLevel.Information);
        this.LogInformation(logger);

        logger.Log("Press Enter key to switch to console mode.");
        logger.Log("Press Ctrl+C to exit.");
        logger.Log("Running");
    }

    public void LogInformation(ILogWriter logger)
    {
        logger.Log($"Utc: {Mics.ToString(Mics.GetUtcNow())}");
        this.LPBase.LogInformation(logger);
    }

    public async Task<bool> TryTerminate(bool forceTerminate = false)
    {
        if (forceTerminate ||
            !this.LPBase.Options.ConfirmExit)
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

    private void RunMachines()
    {
        _ = this.BigMachine.NtpMachine.GetOrCreate().RunAsync();
        _ = this.BigMachine.NetStatsMachine.GetOrCreate().RunAsync();
        _ = this.BigMachine.EssentialNetMachine.GetOrCreate().RunAsync();
        this.BigMachine.LpControlMachine.GetOrCreate(); // .RunAsync();

        if (!string.IsNullOrEmpty(this.LPBase.Options.RelayPeerPrivault))
        {
            this.BigMachine.RelayPeerMachine.GetOrCreate();
        }
    }

    private async Task LoadKeyVault_NodeKey()
    {
        if (!this.Vault.TryGetAndDeserialize<NodePrivateKey>(NetConstants.NodePrivateKeyName, out var key))
        {// Failure
            if (!this.Vault.Created)
            {
                await this.UserInterfaceService.Notify(LogLevel.Error, Hashed.Vault.NoData, NetConstants.NodePrivateKeyName);
            }

            return;
        }

        if (!this.NetControl.NetBase.SetNodePrivateKey(key))
        {
            await this.UserInterfaceService.Notify(LogLevel.Error, Hashed.Vault.NoRestore, NetConstants.NodePrivateKeyName);
            return;
        }
    }

    private async Task TerminateAsync(UnitContext context)
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

    private void Terminate(bool abort)
    {
        this.Core.Terminate();
        this.Core.WaitForTermination(-1);

        this.Logger.Get<DefaultLog>().Log(abort ? "Aborted" : "Terminated");
        this.Logger.FlushAndTerminate().Wait(); // Write logs added after Terminate().
    }
}
