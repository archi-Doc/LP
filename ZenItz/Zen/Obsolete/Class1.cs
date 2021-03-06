// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace ZenItz.Obsolete;

public interface IFlake
{
}

public interface IFragment : IFlake
{
}

[TinyhandUnion(0, typeof(TestFragment))]
public abstract partial class FlakeBase
{
    public bool Check(Identifier identifier)
    {
        try
        {
            var result = TinyhandSerializer.SerializeAndGetMarker(this);
            var id = Identifier.FromReadOnlySpan(result.ByteArray.AsSpan(0, result.MarkerPosition));
            return id.Equals(identifier);
        }
        catch
        {
            return false;
        }
    }
}

public interface IUnprotected
{
}

public readonly struct ZenItem<TFlake, TUnprotected>
    where TFlake : IFlake
    where TUnprotected : IUnprotected
{
    public readonly TFlake Flake;
    public readonly TUnprotected? Unprotected;
}

[TinyhandObject]
public partial class TestFragment : FlakeBase
{
    [Key(0, Marker = true)]
    public string Name { get; private set; } = string.Empty;
}

public class TestUnprotected : IUnprotected
{
}
