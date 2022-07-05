﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace LP.Unit.Sample;

public class TestClass
{
    public static void SampleCode()
    {
        var builder = new TestClass.Builder()
            .Configure(x => { }); // Custom configuration

        var unit = builder.Build();
        unit.RunStandalone(new());

        var built = (BuiltUnit)unit;
        built.Run();
    }

    public class Builder : UnitBuilder<Unit>
    {// Builder class for customizing dependencies.
        public Builder()
            : base()
        {// Configuration
            this.Configure(context =>
            {
                context.AddTransient<TestUnit>();
            });
        }
    }

    public class Unit : BuiltUnit
    {// Unit class for customizing process.
        public record Param();

        public Unit(UnitBuilderContext context)
            : base(context)
        {
        }

        public void RunStandalone(Param param)
        {
        }
    }

    public class TestUnit : UnitBase
    {
        public TestUnit(BuiltUnit controlUnit)
            : base(controlUnit)
        {
        }

        public void Configure()
        {
        }
    }

    public TestClass(TestUnit testUnit)
    {
        this.Unit1 = testUnit;
    }

    public TestUnit Unit1 { get; }
}
