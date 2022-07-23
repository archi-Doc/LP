﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace LP.Logging;

public class LPLogger
{
    public class Builder : UnitBuilder
    {
        private static bool IsSubclassOfRawGeneric(Type? generic, Type? toCheck)
        {
            while (toCheck != null && toCheck != typeof(object))
            {
                var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if (generic == cur)
                {
                    return true;
                }

                toCheck = toCheck.BaseType;
            }

            return false;
        }

        public Builder()
            : base()
        {
            this.Configure(context =>
            {
                // Loggers (ConsoleAndFileLogger, BackgroundAndFileLogger, ConsoleLogger)
                context.AddSingleton<BackgroundAndFileLogger>();
                context.AddSingleton<ConsoleAndFileLogger>();

                // Resolver
                context.ClearLoggerResolver();
                context.AddLoggerResolver(context =>
                {
                    if (context.LogLevel == LogLevel.Debug)
                    {// Debug -> no output
                        context.SetOutput<EmptyLogger>();
                        return;
                    }

                    if (context.LogSourceType == typeof(ConsoleLog))
                    {// Console log
                        context.SetOutput<ConsoleLogger>();
                        return;
                    }
                    else if (IsSubclassOfRawGeneric(typeof(BigMachines.Machine<>), context.LogSourceType))
                    {// Machines
                        context.SetOutput<BackgroundAndFileLogger>();
                        return;
                    }

                    context.SetOutput<ConsoleAndFileLogger>();
                });
            });
        }
    }
}
