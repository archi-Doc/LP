﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using Arc.Visceral;
using Microsoft.CodeAnalysis;

namespace Netsphere.Generator;

public class ServiceMethod
{
    public static ServiceMethod? Create(NetsphereObject machine, NetsphereObject method)
    {
        var flag = false;
        if (method.Method_ReturnObject?.FullName != "Task")
        {// Invalid return type
            flag = true;
        }

        if (flag)
        {
            method.Body.ReportDiagnostic(NetsphereBody.Error_MethodFormat, method.Location);
        }

        if (method.Body.Abort)
        {
            return null;
        }

        var serviceMethod = new ServiceMethod();
        // stateMethod.Location = attribute.Location;
        serviceMethod.Name = method.SimpleName;
        serviceMethod.MethodId = (uint)Arc.Crypto.FarmHash.Hash64(method.FullName);

        return serviceMethod;
    }

    // public Location Location { get; private set; } = Location.None;

    public string Name { get; private set; } = string.Empty;

    public uint MethodId { get; private set; }

    public bool DuplicateId { get; internal set; }
}
