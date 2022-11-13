﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace LP;

public interface ILPSerializable
{
    void Serialize(ref Tinyhand.IO.TinyhandWriter writer);

    bool Deserialize(ReadOnlySpan<byte> span, out int bytesRead);
}
