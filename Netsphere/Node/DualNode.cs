﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Arc.Crypto;
using LP.T3CS;

namespace Netsphere;

/// <summary>
/// Represents ipv4/ipv6 node information.<br/>
/// <see cref="DualNode"/> = <see cref="DualAddress"/> + <see cref="NodePublicKey"/>.
/// </summary>
[TinyhandObject]
public readonly partial struct DualNode : IStringConvertible<DualNode>, IValidatable, IEquatable<DualNode>
{
    public DualNode(DualAddress address, NodePublicKey publicKey)
    {
        this.Address = address;
        this.PublicKey = publicKey;
    }

    [Key(0)]
    public readonly DualAddress Address;

    [Key(1)]
    public readonly NodePublicKey PublicKey;

    public static bool TryParse(ReadOnlySpan<char> source, [MaybeNullWhen(false)] out DualNode instance)
    {// Ip address (public key)
        source = source.Trim();

        var index = source.IndexOf('(');
        if (index < 0)
        {
            instance = default;
            return false;
        }

        var index2 = source.IndexOf(')');
        if (index2 < 0)
        {
            instance = default;
            return false;
        }

        var sourceAddress = source.Slice(0, index);
        var sourcePublicKey = source.Slice(index + 1, index2 - index - 1);

        if (!DualAddress.TryParse(sourceAddress, out var address))
        {
            instance = default;
            return false;
        }

        if (!NodePublicKey.TryParse(sourcePublicKey, out var publicKey))
        {
            instance = default;
            return false;
        }

        instance = new(address, publicKey);
        return true;
    }

    public static int MaxStringLength
        => DualAddress.MaxStringLength + SignaturePublicKey.MaxStringLength + 2;

    public int GetStringLength()
        => throw new NotImplementedException();

    public bool TryFormat(Span<char> destination, out int written)
    {
        var span = destination;
        written = 0;
        if (span.Length < MaxStringLength)
        {
            return false;
        }

        if (!this.Address.TryFormat(span, out written))
        {
            return false;
        }
        else
        {
            span = span.Slice(written);
        }

        span[0] = '(';
        span = span.Slice(1);

        if (!this.PublicKey.TryFormat(span, out written))
        {
            return false;
        }
        else
        {
            span = span.Slice(written);
        }

        span[0] = ')';
        span = span.Slice(1);

        written = destination.Length - span.Length;
        return true;
    }

    public bool Validate()
        => this.Address.Validate() && this.PublicKey.Validate();

    public bool Equals(DualNode other)
        => this.Address.Equals(other.Address) && this.PublicKey.Equals(other.PublicKey);

    public override int GetHashCode()
        => HashCode.Combine(this.Address, this.PublicKey);

    public override string ToString()
    {
        Span<char> span = stackalloc char[MaxStringLength];
        return this.TryFormat(span, out var written) ? span.Slice(0, written).ToString() : string.Empty;
    }
}
