﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Security.Cryptography;

namespace Netsphere;

public class NetBase : UnitBase, IUnitPreparable
{
    public NetBase(UnitContext context, UnitLogger logger)
        : base(context)
    {
        this.logger = logger;
    }

    public void Prepare(UnitMessage.Prepare message)
    {
        // Set port number
        if (this.NetsphereOptions.Port < NetControl.MinPort ||
            this.NetsphereOptions.Port > NetControl.MaxPort)
        {
            var showWarning = false;
            if (this.NetsphereOptions.Port != 0)
            {
                showWarning = true;
            }

            this.NetsphereOptions.Port = LP.Random.Pseudo.NextInt32(NetControl.MinPort, NetControl.MaxPort + 1);
            if (showWarning)
            {
                this.logger.TryGet<NetBase>(LogLevel.Fatal)?.Log($"Port number must be between {NetControl.MinPort} and {NetControl.MaxPort}");
                this.logger.TryGet<NetBase>(LogLevel.Fatal)?.Log($"Port number is set to {this.NetsphereOptions.Port}");
            }
        }

        // Node key
        if (this.NodePrivateKey == null)
        {
            this.NodePrivateKey = PrivateKey.Create(KeyType.Node);
            this.NodePublicKey = new PublicKey(this.NodePrivateKey);
        }
    }

    public bool EnableServer { get; private set; }

    public string NodeName { get; private set; } = default!;

    public NetsphereOptions NetsphereOptions { get; private set; } = default!;

    public bool AllowUnsafeConnection { get; set; } = false;

    public PublicKey NodePublicKey { get; private set; } = default!;

    public class LogFlag
    {
        public bool FlowControl { get; set; }
    }

    public LogFlag Log { get; } = new();

    public void SetParameter(bool enableServer, string nodeName, NetsphereOptions netsphereOptions)
    {
        this.EnableServer = enableServer;
        this.NodeName = nodeName;
        if (string.IsNullOrEmpty(this.NodeName))
        {
            this.NodeName = System.Environment.OSVersion.ToString();
        }

        this.NetsphereOptions = netsphereOptions;
    }

    public bool SetNodeKey(PrivateKey privateKey)
    {
        try
        {
            this.NodePublicKey = new PublicKey(privateKey);
            this.NodePrivateKey = privateKey;
            return true;
        }
        catch
        {
            this.NodePublicKey = default!;
            this.NodePrivateKey = default!;
            return false;
        }
    }

    public byte[] SerializeNodeKey()
    {
        return TinyhandSerializer.Serialize(this.NodePrivateKey);
    }

    public override string ToString() => $"NetBase: {this.NodeName}";

    internal PrivateKey NodePrivateKey { get; private set; } = default!;

    private UnitLogger logger;
}
