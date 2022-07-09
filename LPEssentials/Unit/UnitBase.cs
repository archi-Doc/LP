﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Threading;
using static LP.Unit.Sample.TestClass;

namespace LP.Unit;

/// <summary>
/// Base class of Unit.<br/>
/// Unit is an independent unit of function and dependency.
/// </summary>
public abstract class UnitBase
{
    public UnitBase(UnitParameter parameter)
    {
        var radio = parameter.Radio;

        if (this is IUnitConfigurable configurable)
        {
            radio.Open<UnitMessage.Configure>(x => configurable.Configure(x), this);
        }

        if (this is IUnitExecutable executable)
        {
            radio.OpenAsync<UnitMessage.RunAsync>(x => executable.RunAsync(x), this);
            radio.OpenAsync<UnitMessage.TerminateAsync>(x => executable.TerminateAsync(x), this);
        }

        if (this is IUnitSerializable serializable)
        {
            radio.OpenAsync<UnitMessage.LoadAsync>(x => serializable.LoadAsync(x), this);
            radio.OpenAsync<UnitMessage.SaveAsync>(x => serializable.SaveAsync(x), this);
        }
    }

    /*public UnitBase(BuiltUnit? builtUnit)
    {
        this.BuiltUnit = builtUnit;
        this.BuiltUnit?.AddInternal(this);
    }*/

    // public BuiltUnit? BuiltUnit { get; }
}

public interface IUnitConfigurable
{
    public void Configure(UnitMessage.Configure message);
}

public interface IUnitExecutable
{
    public Task RunAsync(UnitMessage.RunAsync message);

    public Task TerminateAsync(UnitMessage.TerminateAsync message);
}

public interface IUnitSerializable
{
    public Task LoadAsync(UnitMessage.LoadAsync message);

    public Task SaveAsync(UnitMessage.SaveAsync message);
}
