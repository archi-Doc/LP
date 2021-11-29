﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Arc.Crypto;
using Arc.Threading;
using DryIoc;
using LP;
using LP.Net;
using LP.Options;
using SimpleCommandLine;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace LPConsole;

[SimpleCommand("lp", Default = true)]
public class LPConsoleCommand : ISimpleCommandAsync<LPConsoleOptions>
{
    public LPConsoleCommand()
    {
    }

    public async Task Run(LPConsoleOptions option, string[] args)
    {
        this.information = Program.Container.Resolve<Information>();
        this.information.Initialize(option, true, "relay");

        this.netBase = Program.Container.Resolve<NetBase>();
        this.netBase.Initialize(string.Empty, option.NetsphereOptions);

        if (await this.LoadAsync() == AbortOrComplete.Abort)
        {
            goto Abort;
        }

        var control = Program.Container.Resolve<Control>();
        control.Configure();
        await control.LoadAsync();

        if (!control.TryStart())
        {
            goto Abort;
        }

        this.MainLoop(control);

        control.Stop();
        await control.SaveAsync();
        control.Terminate();

Abort:
        Logger.Default.Information("LP Aborted");
        return;
    }

    private async Task<AbortOrComplete> LoadAsync()
    {
        // Load node key.
        if (await this.LoadNodeKey() == AbortOrComplete.Abort)
        {
            return AbortOrComplete.Abort;
        }

        return AbortOrComplete.Complete;
    }

    private string? GetPassword(string? text = null)
    {
        if (text == null)
        {
            text = "Enter password: ";
        }

        Console.Write(text);

        ConsoleKey key;
        var password = string.Empty;
        do
        {
            var keyInfo = Console.ReadKey(intercept: true);
            key = keyInfo.Key;
            if (ThreadCore.Root.IsTerminated)
            {
                return null;
            }

            if (key == ConsoleKey.Backspace && password.Length > 0)
            {
                Console.Write("\b \b");
                password = password[0..^1];
            }
            else if (!char.IsControl(keyInfo.KeyChar))
            {
                Console.Write("*");
                password += keyInfo.KeyChar;
            }
            else if (key == ConsoleKey.Escape)
            {
                return null;
            }
        }
        while (key != ConsoleKey.Enter);

        Console.WriteLine();
        return password;
    }

    private async Task<AbortOrComplete> LoadNodeKey()
    {
        var file = NodePrivateKey.Filename;
        if (!File.Exists(file))
        {
            return AbortOrComplete.Complete;
        }

        try
        {
            var encrypted = File.ReadAllBytes(file);
            Memory<byte> data;
            if (PasswordEncrypt.TryDecrypt(encrypted, string.Empty, out data))
            {
                goto Deserialize;
            }

            Console.WriteLine(file);
EnterNodeKeyPassword:
            var password = this.GetPassword();
            if (password == null)
            {
                Console.WriteLine();
                return AbortOrComplete.Abort;
            }
            else if (!PasswordEncrypt.TryDecrypt(encrypted, password, out data))
            {// Incorrect
                Console.WriteLine("Incorrect password.");
                Console.WriteLine();
                goto EnterNodeKeyPassword;
            }
            else
            {// Correct
                Console.WriteLine();
            }

Deserialize:
            var key = Tinyhand.TinyhandSerializer.Deserialize<NodePrivateKey>(data);
            if (key != null)
            {
                this.netBase.SetNodeKey(key);
                Logger.Default.Information($"Loaded: {file}");
            }
        }
        catch
        {
        }

        return AbortOrComplete.Complete;
    }

    private void MainLoop(Control control)
    {
        while (!control.Core.IsTerminated)
        {
            if (Logger.ViewMode)
            {// View mode
                if (this.SafeKeyAvailable)
                {
                    var keyInfo = Console.ReadKey(true);
                    if (keyInfo.Key == ConsoleKey.Enter || keyInfo.Key == ConsoleKey.Escape)
                    { // To console mode
                        Logger.ViewMode = false;
                        Console.Write("> ");
                    }
                    else
                    {
                        while (this.SafeKeyAvailable)
                        {
                            Console.ReadKey(true);
                        }
                    }
                }
            }
            else
            {// Console mode
                var command = Console.ReadLine();
                if (!string.IsNullOrEmpty(command))
                {
                    if (string.Compare(command, "exit", true) == 0)
                    {// Exit
                        // To view mode
                        Logger.ViewMode = true;
                        return;
                    }
                    else
                    {
                        if (!control.Subcommand(command))
                        {
                            Console.Write("> ");
                            continue;
                        }
                    }
                }

                // To view mode
                Logger.ViewMode = true;
            }

            control.Core.Sleep(100, 100);
        }
    }

    private bool SafeKeyAvailable
    {
        get
        {
            try
            {
                return Console.KeyAvailable;
            }
            catch
            {
                return false;
            }
        }
    }

    private Information information = default!;

    private NetBase netBase = default!;
}
