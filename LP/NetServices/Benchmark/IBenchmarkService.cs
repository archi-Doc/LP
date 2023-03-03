﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;
using Tinyhand;

namespace LP.NetServices;

[NetServiceInterface]
public partial interface IBenchmarkService : INetService
{
    public NetTask Send(byte[] data);

    public NetTask<byte[]?> Pingpong(byte[] data);

    public NetTask<NetResult> Register();

    public NetTask<NetResult> Start(int total, int concurrent);

    [TinyhandObject(ImplicitKeyAsName = true)]
    public partial record ReportRecord
    {
        public int SuccessCount { get; init; }

        public int FailureCount { get; init; }

        public int Concurrent { get; init; }

        public long ElapsedMilliseconds { get; init; }

        public int CountPerSecond { get; init; }

        public int AverageLatency { get; init; }

        public override string ToString()
            => $"Success/Failure {this.SuccessCount}/{this.FailureCount}, {this.ElapsedMilliseconds} ms, {this.CountPerSecond} c/s, latency {this.AverageLatency} ms";
    }

    public NetTask Report(ReportRecord record);
}
