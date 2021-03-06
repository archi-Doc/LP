// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace ZenItz;

public interface IItzShip<TPayload> : IItzShip
    where TPayload : IItzPayload
{
    void Set(in Identifier id, in TPayload value);

    ItzResult Get(in Identifier id, out TPayload value);
}

public interface IItzShip : ILPSerializable
{
    int Count();
}
