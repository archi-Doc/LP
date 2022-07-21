﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Arc.Unit;

public interface ILogger
{
    public void Log(string message);

    public Type OutputType { get; }
}
