﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1210 // Using directives should be ordered alphabetically by namespace
#pragma warning disable SA1208 // System using directives should be placed before other using directives
global using System;
global using System.IO;
global using Arc.Collections;
global using Arc.Crypto;
global using CrossChannel;
global using Tinyhand;

using LP.Options;
using System.Security.Cryptography;

namespace LP;

public enum LPMode
{
    Relay,
    Merger,
    User,
}

public class Information
{
    public Information()
    {
    }

    public bool IsConsole { get; private set; }

    public string RootDirectory { get; private set; } = default!;

    public LPMode Mode { get; private set; }

    public string NodeName { get; private set; } = default!;

    public LPConsoleOptions ConsoleOptions { get; private set; } = default!;

    public void Initialize(LPConsoleOptions options, bool isConsole, string defaultMode)
    {
        this.ConsoleOptions = options;
        this.IsConsole = isConsole;

        // Root directory
        if (Path.IsPathRooted(this.ConsoleOptions.Directory) &&
            File.GetAttributes(this.ConsoleOptions.Directory).HasFlag(FileAttributes.Directory))
        {
            this.RootDirectory = this.ConsoleOptions.Directory;
        }
        else
        {
            this.RootDirectory = Path.Combine(Directory.GetCurrentDirectory(), this.ConsoleOptions.Directory);
        }

        Directory.CreateDirectory(this.RootDirectory);

        // Mode
        LPMode mode;
        if (!Enum.TryParse<LPMode>(this.ConsoleOptions.Mode, true, out mode))
        {
            if (!Enum.TryParse<LPMode>(defaultMode, true, out mode))
            {
                mode = LPMode.Merger;
            }
        }

        this.Mode = mode;

        this.NodeName = this.ConsoleOptions.NodeName;
        if (string.IsNullOrEmpty(this.NodeName))
        {
            this.NodeName = System.Environment.OSVersion.ToString();
        }
    }

    public override string ToString()
    {
        return $"Mode: {this.Mode}, {this.ConsoleOptions.ToString()}";
    }
}
