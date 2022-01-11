// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Netsphere;

internal abstract class NetOperation : IAsyncDisposable
{
    internal NetOperation(NetTerminal netTerminal)
    {
        this.Terminal = netTerminal.Terminal;
        this.NetTerminal = netTerminal;
    }

    public ulong GetGene()
    {
        if (this.genePool == null)
        {
            this.genePool = this.NetTerminal.TryFork();
            this.Terminal.TerminalLogger?.Information($"TryFork1 - {this.NetTerminal.IsEncrypted}"); // temporary
        }

        var gp = this.genePool != null ? this.genePool : this.NetTerminal.GenePool;
        // return gp.GetSequential();

        var x = gp.GetSequential();
        this.Terminal.TerminalLogger?.Information(x.To4Hex());
        return x;
    }

    public (ulong First, ulong Second) Get2Genes()
    {
        if (this.genePool == null)
        {
            this.genePool = this.NetTerminal.TryFork();
            this.Terminal.TerminalLogger?.Information($"TryFork2 - {this.NetTerminal.IsEncrypted}"); // temporary
        }

        var gp = this.genePool != null ? this.genePool : this.NetTerminal.GenePool;
        // return gp.GetSequential2();

        var x = gp.GetSequential2();
        this.Terminal.TerminalLogger?.Information($"{x.First.To4Hex()} - {x.Second.To4Hex()}");
        return x;
    }

    public void GetGenes(Span<ulong> span)
    {
        if (this.genePool == null)
        {
            this.genePool = this.NetTerminal.TryFork();
            this.Terminal.TerminalLogger?.Information($"TryFork3 - {this.NetTerminal.IsEncrypted}"); // temporary
        }

        var gp = this.genePool != null ? this.genePool : this.NetTerminal.GenePool;
        // gp.GetSequential(span);

        gp.GetSequential(span);
        this.Terminal.TerminalLogger?.Information($"Span {span.Length}");
    }

    public virtual async Task<NetResult> EncryptConnectionAsync() => NetResult.NoEncryptedConnection;

    public Terminal Terminal { get; }

    public NetTerminal NetTerminal { get; }

    private GenePool? genePool;

#pragma warning disable SA1124 // Do not use regions
    #region IDisposable Support
#pragma warning restore SA1124 // Do not use regions

    private bool disposed = false; // To detect redundant calls.

    /// <summary>
    /// Finalizes an instance of the <see cref="NetOperation"/> class.
    /// </summary>
    ~NetOperation()
    {
        this.Dispose(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// free managed/native resources.
    /// </summary>
    /// <param name="disposing">true: free managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed)
        {
            if (disposing)
            {
                // free managed resources.
                this.genePool?.Dispose();
                this.genePool = null;
            }

            // free native resources here if there are any.
            this.disposed = true;
        }
    }
    #endregion
}
