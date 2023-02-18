﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace CrystalData;

public record ZenOptions
{
    public const long DefaultDirectoryCapacity = 1024L * 1024 * 1024 * 10; // 10GB
    public const int DefaultMaxDataSize = 1024 * 1024 * 4; // 4MB
    public const int DefaultMaxFragmentSize = 1024 * 4; // 4KB
    public const int DefaultMaxFragmentCount = 1000;
    public const int DefaultMemorySizeLimit = 1024 * 1024 * 500; // 500MB

    public static ZenOptions Default { get; } = new ZenOptions();

    /// <summary>
    /// Initializes a new instance of the <see cref="ZenOptions"/> class.
    /// </summary>
    public ZenOptions()
    {
    }

    /// <summary>
    /// Gets a path of the directory where <see cref="Zen"/> files are stored.
    /// </summary>
    public string ZenPath { get; init; } = string.Empty;

    public int MaxDataSize { get; init; } = DefaultMaxDataSize;

    public int MaxFragmentSize { get; init; } = DefaultMaxFragmentSize;

    public int MaxFragmentCount { get; init; } = DefaultMaxFragmentCount;

    public long MemorySizeLimit { get; init; } = DefaultMemorySizeLimit;

    public long DirectoryCapacity { get; init; } = 1024L * 1024 * 1024 * 10; // 10GB

    public string ZenFile { get; init; } = "Zen.main";

    public string ZenBackup { get; init; } = "Zen.back";

    public string ZenDirectoryFile { get; init; } = "ZenDirectory.main";

    public string ZenDirectoryBackup { get; init; } = "ZenDirectory.back";

    public string DefaultZenDirectory { get; init; } = "Zen";

    public string SnowflakeFile { get; init; } = "Snowflake.main";

    public string SnowflakeBackup { get; init; } = "Snowflake.back";

    public string RootPath => this.rootPath ??= PathHelper.GetRootedDirectory(Directory.GetCurrentDirectory(), this.ZenPath);

    public string ZenFilePath => PathHelper.GetRootedFile(this.RootPath, this.ZenFile);

    public string ZenBackupPath => PathHelper.GetRootedFile(this.RootPath, this.ZenBackup);

    public string ZenDirectoryFilePath => PathHelper.GetRootedFile(this.RootPath, this.ZenDirectoryFile);

    public string ZenDirectoryBackupPath => PathHelper.GetRootedFile(this.RootPath, this.ZenDirectoryBackup);

    private string? rootPath;
}
