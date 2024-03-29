﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace LP.Machines;

[MachineObject(UseServiceProvider = true)]
// [TinyhandObject(UseServiceProvider = true)]
public partial class SingleMachine : Machine
{
    public SingleMachine(IUserInterfaceService consoleSeuserInterfaceServicevice)
    {
        this.userInterfaceService = consoleSeuserInterfaceServicevice;
        this.TimeUntilRun = TimeSpan.FromSeconds(1);
    }

    public int Count { get; set; }

    [StateMethod(0)]
    protected StateResult Initial(StateParameter parameter)
    {
        // this.userInterfaceService.WriteLine($"Single: ({this.Identifier.ToString()}) - {this.Count++}");

        return StateResult.Continue;
    }

    private IUserInterfaceService userInterfaceService;
}
