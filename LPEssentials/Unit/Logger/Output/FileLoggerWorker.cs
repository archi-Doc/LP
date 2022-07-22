﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using Arc.Threading;
using static Arc.Unit.ConsoleLoggerWorker;

namespace Arc.Unit;

internal class FileLoggerWorker : TaskCore
{
    private const int MaxFlush = 10_000;

    public FileLoggerWorker(string path, SimpleLogFormatterOptions options)
        : base(ThreadCore.Root, Process)
    {
        this.path = path;
        this.formatter = new(options);
    }

    public static async Task Process(object? obj)
    {
        var worker = (FileLoggerWorker)obj!;
        while (worker.Sleep(1000))
        {
            await worker.Flush();
        }
    }

    public void Add(FileLoggerWork work)
    {
        this.queue.Enqueue(work);
    }

    public async Task<int> Flush()
    {
        if (this.Count == 0)
        {
            return 0;
        }

        await this.semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            StringBuilder sb = new();
            var count = 0;
            while (count++ < MaxFlush && this.queue.TryDequeue(out var work))
            {
                this.formatter.Format(sb, work.Parameter);
                sb.Append(Environment.NewLine);
            }

            if (count != 0)
            {
                await File.AppendAllTextAsync(this.path, sb.ToString());
            }

            return count;
        }
        finally
        {
            this.semaphore.Release();
        }
    }

    public int Count => this.queue.Count;

    private string path;
    private SimpleLogFormatter formatter;
    private ConcurrentQueue<FileLoggerWork> queue = new();
    private SemaphoreSlim semaphore = new(1, 1);
}

internal class FileLoggerWork
{
    public FileLoggerWork(LogOutputParameter parameter)
    {
        this.Parameter = parameter;
    }

    public LogOutputParameter Parameter { get; }
}
