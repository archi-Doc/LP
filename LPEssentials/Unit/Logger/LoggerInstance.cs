﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;

namespace Arc.Unit;

internal class LoggerInstance : ILogger
{
    /*private readonly struct ObjectMethodKey : IEquatable<ObjectMethodKey>
    {
        public ObjectMethodKey(object obj, string method)
        {
            this.Object = obj;
            this.Method = method;
        }

        public readonly object Object;

        public readonly string Method;

        public bool Equals(ObjectMethodKey other)
            => this.Object == other.Object &&
                this.Method == other.Method;

        public override int GetHashCode()
            => HashCode.Combine(this.Object, this.Method);
    }*/

    public LoggerInstance(Type logSourceType, LogLevel logLevel, ILogOutput logOutput, ILogFilter? logFilter)
    {
        this.OutputType = logOutput.GetType();
        this.logSourceType = logSourceType;
        this.logLevel = logLevel;

        this.logDelegate = (ILogOutput.OutputDelegate)delegateCache.GetOrAdd(logOutput, static x =>
        {
            Console.WriteLine("Logger instance");
            var type = x.GetType();
            var method = type.GetMethod(nameof(ILogOutput.Output));
            if (method == null)
            {
                throw new ArgumentException();
            }

            return Delegate.CreateDelegate(typeof(ILogOutput.OutputDelegate), x, method);
        });

        if (logFilter != null)
        {
            this.filterDelegate = (ILogFilter.FilterDelegate)delegateCache.GetOrAdd(logFilter, static x =>
            {
                var type = x.GetType();
                var method = type.GetMethod(nameof(ILogFilter.Filter));
                if (method == null)
                {
                    throw new ArgumentException();
                }

                return Delegate.CreateDelegate(typeof(ILogFilter.FilterDelegate), x, method);
            });
        }

        /*this.logDelegate = (ILogOutput.OutputDelegate)delegateCache.GetOrAdd(new(logOutput, logLevel.ToString()), static x =>
        {
            Console.WriteLine("Logger instance");
            var type = x.Object.GetType();
            var method = type.GetMethod(x.Method);
            if (method == null)
            {
                throw new ArgumentException();
            }

            return Delegate.CreateDelegate(typeof(ILogOutput.OutputDelegate), x.Object, method);
        });

        if (logFilter != null)
        {
            this.filterDelegate = (ILogFilter.FilterDelegate)delegateCache.GetOrAdd(new(logFilter, nameof(ILogFilter.Filter)), static x =>
            {
                var type = x.Object.GetType();
                var method = type.GetMethod(x.Method);
                if (method == null)
                {
                    throw new ArgumentException();
                }

                return Delegate.CreateDelegate(typeof(ILogFilter.FilterDelegate), x.Object, method);
            });
        }*/
    }

    public void Log(string message)
    {
        if (this.filterDelegate != null)
        {// Filter -> Log
            if (this.filterDelegate(new(this.logSourceType, this.logLevel, this)) is LoggerInstance loggerInstance)
            {
                loggerInstance.logDelegate(this.logSourceType, this.logLevel, message);
            }
        }
        else
        {// Log
            this.logDelegate(this.logSourceType, this.logLevel, message);
        }
    }

    public Type OutputType { get; }

    private static ConcurrentDictionary<object, Delegate> delegateCache = new();

    private Type logSourceType;
    private LogLevel logLevel;
    private ILogOutput.OutputDelegate logDelegate;
    private ILogFilter.FilterDelegate? filterDelegate;
}
