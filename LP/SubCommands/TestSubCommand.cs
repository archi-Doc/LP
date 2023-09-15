﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Security.Cryptography;
using LP.NetServices;
using LP.T3CS;
using SimpleCommandLine;

namespace LP.Subcommands;

[SimpleCommand("test")]
public class TestSubcommand : ISimpleCommandAsync<TestOptions>
{
    public TestSubcommand(ILogger<TestSubcommand> logger, IUserInterfaceService userInterfaceService, Control control, Seedphrase seedPhrase)
    {
        this.logger = logger;
        this.userInterfaceService = userInterfaceService;
        this.control = control;
        this.seedPhrase = seedPhrase;
    }

    public async Task RunAsync(TestOptions options, string[] args)
    {
        this.logger.TryGet()?.Log($"Test subcommand: {options.ToString()}");

        await this.TestLinkageKey();
    }

    private async Task TestLinkageKey()
    {
        var privateKey = PrivateKey.CreateVerificationKey();
        var publicKey = privateKey.ToPublicKey();
        this.userInterfaceService.WriteLine($"Private(verification): {privateKey.ToUnsafeString()}");
        this.userInterfaceService.WriteLine($"Public(verification): {publicKey.ToString()}");

        var privateKey2 = PrivateKey.CreateEncryptionKey();
        var publicKey2 = privateKey2.ToPublicKey();
        this.userInterfaceService.WriteLine($"Private(encryption): {privateKey2.ToUnsafeString()}");
        this.userInterfaceService.WriteLine($"Public(encryption): {publicKey2.ToString()}");

        var rawKey = publicKey.ToLinkageKey();
        this.userInterfaceService.WriteLine($"Raw: {rawKey.ToString()}");
        var encryptedKey = publicKey.ToLinkageKey(publicKey2);
        this.userInterfaceService.WriteLine($"Encrypted: {encryptedKey.ToString()}");
    }

    private async Task Test0()
    {
        ECParameters key = default;
        key.Curve = ECCurve.CreateFromFriendlyName(PublicKey.CurveInstance.CurveName);

        var st = this.seedPhrase.Create();
        var seed = this.seedPhrase.TryGetSeed(st);
        if (seed != null)
        {
            var pk = PrivateKey.CreateVerificationKey(seed);
        }

        var privateKey = NodePrivateKey.AlternativePrivateKey;
        var publicKey = privateKey.ToPublicKey();

        this.userInterfaceService.WriteLine($"Alternative(private): {privateKey.ToUnsafeString()}");
        this.userInterfaceService.WriteLine($"Length: {TinyhandSerializer.Serialize(privateKey).Length.ToString()}");
        this.userInterfaceService.WriteLine(TinyhandSerializer.SerializeToString(privateKey));
        this.userInterfaceService.WriteLine();

        this.userInterfaceService.WriteLine($"Alternative(public): {publicKey.ToString()}");
        this.userInterfaceService.WriteLine($"Length: {TinyhandSerializer.Serialize(publicKey).Length.ToString()}");
        this.userInterfaceService.WriteLine(TinyhandSerializer.SerializeToString(publicKey));

        var originator = PrivateKey.Create("originator");
        var pub = originator.ToPublicKey();
        var value = new Value(1, pub, new[] { pub, });
        this.userInterfaceService.WriteLine(value.GetHashCode().ToString());

        var bin = TinyhandSerializer.Serialize(value);
        var sign = originator.SignData(bin);
        var flag = pub.VerifyData(bin, sign);

        this.userInterfaceService.WriteLine($"Originator: {originator.ToString()}, {flag.ToString()}");
        this.userInterfaceService.WriteLine($"{pub.ToString()}");

        var token = new Token(Token.Type.Identification, 0, 0, Identifier.Three, null);
        var bb = token.Sign(originator);
        bb = token.ValidateAndVerifyWithoutPublicKey();

        originator.CreateSignature(value, out var signature);
        // this.userInterfaceService.WriteLine(HashedString.FromEnum(CrystalResult.NoStorage));

        using (var terminal = this.control.NetControl.Terminal.Create(Netsphere.NodeAddress.Alternative))
        {
            var service = terminal.GetService<IBenchmarkService>();
            await service.Report(new());
        }
    }

    private ILogger<TestSubcommand> logger;
    private Control control;
    private IUserInterfaceService userInterfaceService;
    private Seedphrase seedPhrase;
}

public record TestOptions
{
    [SimpleOption("node", Description = "Node address")]
    public string Node { get; init; } = string.Empty;

    [SimpleOption("count", Description = "Count")]
    public int Count { get; init; }

    public override string ToString() => $"{this.Node}";
}
