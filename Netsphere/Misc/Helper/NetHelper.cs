﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Netsphere.Crypto;
using Netsphere.Packet;
using Tinyhand.IO;

#pragma warning disable SA1202

namespace Netsphere;

public static class NetHelper
{
    internal const int BurstGenes = 3;
    internal const char Quote = '\"';
    internal const string TripleQuotes = "\"\"\"";
    private const int StreamBufferSize = 1024 * 1024 * 4; // 4 MB

    public static async Task<NetNode?> TryGetNetNode(NetTerminal netTerminal, string nodeString)
    {
        if (NetNode.TryParse(nodeString, out var netNode))
        {
            return netNode;
        }

        if (!NetAddress.TryParse(nodeString, out var netAddress))
        {
            return null;
        }

        return await netTerminal.UnsafeGetNetNode(netAddress).ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsError(this NetResult result)
        => result != NetResult.Success && result != NetResult.Completed;

    public static async Task<NetResult> ReceiveStreamToStream(ReceiveStream receiveStream, Stream stream, CancellationToken cancellationToken = default)
    {
        var result = NetResult.UnknownError;
        var buffer = ArrayPool<byte>.Shared.Rent(StreamBufferSize);
        try
        {
            while (true)
            {
                (result, var written) = await receiveStream.Receive(buffer, cancellationToken).ConfigureAwait(false);
                if (written > 0)
                {
                    await stream.WriteAsync(buffer.AsMemory(0, written), cancellationToken).ConfigureAwait(false);
                }

                if (result == NetResult.Success)
                {// Continue
                }
                else if (result == NetResult.Completed)
                {// Completed
                    result = NetResult.Success;
                    break;
                }
                else
                {// Error
                    break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return result;
    }

    public static async Task<NetResult> ReceiveStreamToStream<TResponse>(ReceiveStream<TResponse> receiveStream, Stream stream, CancellationToken cancellationToken = default)
    {
        var result = NetResult.UnknownError;
        var buffer = ArrayPool<byte>.Shared.Rent(StreamBufferSize);
        try
        {
            while (true)
            {
                (result, var written) = await receiveStream.Receive(buffer, cancellationToken).ConfigureAwait(false);
                if (written > 0)
                {
                    await stream.WriteAsync(buffer.AsMemory(0, written), cancellationToken).ConfigureAwait(false);
                }

                if (result == NetResult.Success)
                {// Continue
                }
                else if (result == NetResult.Completed)
                {// Completed
                    result = NetResult.Success;
                    break;
                }
                else
                {// Error
                    break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return result;
    }

    public static async Task<NetResult> StreamToSendStream(Stream stream, SendStream sendStream, CancellationToken cancellationToken = default)
    {
        var result = NetResult.Success;
        var buffer = ArrayPool<byte>.Shared.Rent(StreamBufferSize);
        long totalSent = 0;
        try
        {
            int length;
            while ((length = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                result = await sendStream.Send(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                if (result.IsError())
                {
                    return result;
                }

                totalSent += length;
            }

            await sendStream.Complete(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await sendStream.Cancel(cancellationToken);
            result = NetResult.Canceled;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return result;
    }

    public static async Task<NetResultValue<TReceive>> StreamToSendStream<TReceive>(Stream stream, SendStreamAndReceive<TReceive> sendStream, CancellationToken cancellationToken = default)
    {
        var result = NetResult.Success;
        var buffer = ArrayPool<byte>.Shared.Rent(StreamBufferSize);
        long totalSent = 0;
        try
        {
            int length;
            while ((length = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                result = await sendStream.Send(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                if (result.IsError())
                {
                    return new(result);
                }

                totalSent += length;
            }

            var r = await sendStream.CompleteSendAndReceive(cancellationToken).ConfigureAwait(false);
            return r;
        }
        catch
        {
            await sendStream.Cancel(cancellationToken);
            result = NetResult.Canceled;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return new(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeserializeNetResult(ulong dataId, ReadOnlySpan<byte> span, out NetResult value)
    {
        if (span.Length == 1)
        {
            value = (NetResult)span[0];
        }
        else
        {
            value = (NetResult)dataId;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryDeserializeNetResult(ReadOnlySpan<byte> span, out NetResult value)
    {
        if (span.Length == 1)
        {
            value = (NetResult)span[0];
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SerializeNetResult(NetResult value, out BytePool.RentMemory rentMemory)
    {
        rentMemory = BytePool.Default.Rent(1).AsMemory();
        rentMemory.Span[0] = (byte)value;
    }

    public static bool TrySerialize<T>(T value, out BytePool.RentMemory rentMemory)
    {
        var writer = TinyhandWriter.CreateFromBytePool();
        try
        {
            TinyhandSerializer.Serialize(ref writer, value, TinyhandSerializerOptions.Standard);
            rentMemory = writer.FlushAndGetRentMemory();
            return true;
        }
        catch
        {
            rentMemory = default;
            return false;
        }
        finally
        {
            writer.Dispose();
        }
    }

    public static bool TrySerializeWithLength<T>(T value, out BytePool.RentMemory rentMemory)
    {
        var writer = TinyhandWriter.CreateFromBytePool();
        try
        {
            writer.Advance(4); // sizeof(int)
            TinyhandSerializer.Serialize(ref writer, value, TinyhandSerializerOptions.Standard);

            rentMemory = writer.FlushAndGetRentMemory();
            BitConverter.TryWriteBytes(rentMemory.Span, rentMemory.Length - sizeof(int));
            return true;
        }
        catch
        {
            rentMemory = default;
            return false;
        }
    }

    public static bool TryDeserialize<T>(BytePool.RentMemory rentMemory, [MaybeNullWhen(false)] out T value)
        => TinyhandSerializer.TryDeserialize<T>(rentMemory.Memory.Span, out value, TinyhandSerializerOptions.Standard);

    public static bool TryDeserialize<T>(BytePool.RentReadOnlyMemory rentMemory, [MaybeNullWhen(false)] out T value)
        => TinyhandSerializer.TryDeserialize<T>(rentMemory.Memory.Span, out value, TinyhandSerializerOptions.Standard);

    public static bool Sign<T>(this T value, SignaturePrivateKey privateKey)
        where T : ITinyhandSerialize<T>, ISignAndVerify
    {
        var ecdsa = privateKey.TryGetEcdsa();
        if (ecdsa == null)
        {
            return false;
        }

        var writer = TinyhandWriter.CreateFromBytePool();
        writer.Level = 0;
        try
        {
            value.PublicKey = privateKey.ToPublicKey();
            value.SignedMics = Mics.GetCorrected(); // signedMics;
            TinyhandSerializer.SerializeObject(ref writer, value, TinyhandSerializerOptions.Signature);
            Span<byte> hash = stackalloc byte[32];
            var rentMemory = writer.FlushAndGetRentMemory();
            Sha3Helper.Get256_Span(rentMemory.Span, hash);
            rentMemory.Return();

            var sign = new byte[KeyHelper.SignatureLength];
            if (!ecdsa.TrySignHash(hash, sign.AsSpan(), out var written))
            {
                return false;
            }

            value.Signature = sign; // value.SetSignInternal(sign);
            return true;
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>
    /// Validate object members and verify that the signature is appropriate.
    /// </summary>
    /// <param name="value">The object to be verified.</param>
    /// <typeparam name="T">The type of the object.</typeparam>
    /// <returns><see langword="true" />: Success.</returns>
    public static bool ValidateAndVerify<T>(T value)
        where T : ITinyhandSerialize<T>, ISignAndVerify
    {
        if (!value.Validate())
        {
            return false;
        }

        var buffer = RentBuffer();
        var writer = new TinyhandWriter(buffer) { Level = 0, };
        try
        {
            TinyhandSerializer.SerializeObject(ref writer, value, TinyhandSerializerOptions.Signature);
            writer.FlushAndGetReadOnlySpan(out var span, out _);
            return value.PublicKey.VerifyData(span, value.Signature);
        }
        finally
        {
            writer.Dispose();
            ReturnBuffer(buffer);
        }
    }

    public static ulong GetDataId<TSend, TReceive>()
        => (ulong)Tinyhand.TinyhandHelper.GetFullNameId<TSend>() | ((ulong)Tinyhand.TinyhandHelper.GetFullNameId<TReceive>() << 32);

    public static string ToBase64<T>(this T value)
        where T : ITinyhandSerialize<T>
    {
        return Base64.Url.FromByteArrayToString(TinyhandSerializer.SerializeObject(value));
    }

    public static string To4Hex(this ulong gene) => $"{(ushort)gene:x4}";

    public static string To4Hex(this uint id) => $"{(ushort)id:x4}";

    public static string TrimQuotes(this string text)
    {
        if (text.Length >= 6 && text.StartsWith(TripleQuotes) && text.EndsWith(TripleQuotes))
        {
            return text.Substring(3, text.Length - 6);
        }
        else if (text.Length >= 2 && text.StartsWith(Quote) && text.EndsWith(Quote))
        {
            return text.Substring(1, text.Length - 2);
        }

        return text;
    }

    public static async Task<(ClientConnection? Connection, TService? Service)> TryGetStreamService<TService>(NetTerminal netTerminal, string node, string remotePrivateKey, long maxStreamLength)
        where TService : INetService, INetServiceAgreement
    {
        // 1st: node, 2nd: EnvironmentVariable 'node'
        if (!NetNode.TryParse(node, out var netNode))
        {
            if (!CryptoHelper.TryParseFromEnvironmentVariable<NetNode>(NetConstants.NodeName, out netNode))
            {
                if (node == NetAddress.AlternativeName ||
                    Environment.GetEnvironmentVariable(NetConstants.NodeName) == NetAddress.AlternativeName)
                {
                    netNode = await netTerminal.UnsafeGetNetNode(NetAddress.Alternative).ConfigureAwait(false);
                }

                if (netNode is null)
                {
                    return default;
                }
            }
        }

        // 1st: remotePrivateKey, 2nd: EnvironmentVariable 'remoteprivatekey'
        if (!SignaturePrivateKey.TryParse(remotePrivateKey, out var signaturePrivateKey))
        {
            if (!CryptoHelper.TryParseFromEnvironmentVariable<SignaturePrivateKey>(NetConstants.RemotePrivateKeyName, out signaturePrivateKey))
            {
                return default;
            }
        }

        var connection = await netTerminal.Connect(netNode).ConfigureAwait(false);
        if (connection == null)
        {
            return default;
        }

        var service = connection.GetService<TService>();

        var agreement = connection.Agreement with { MaxStreamLength = maxStreamLength, };
        var token = new CertificateToken<ConnectionAgreement>(agreement);
        if (!connection.SignWithSalt(token, signaturePrivateKey))
        {
            connection.Dispose();
            return default;
        }

        var result = await service.UpdateAgreement(token).ValueAsync.ConfigureAwait(false);
        if (result != NetResult.Success)
        {
            connection.Dispose();
            return default;
        }

        return (connection, service);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static (int NumberOfGenes, uint FirstGeneSize, uint LastGeneSize) CalculateGene(long size)
    {// FirstGeneSize, GeneFrame.MaxBlockLength..., LastGeneSize
        if (size <= FirstGeneFrame.MaxGeneLength)
        {
            return (1, (uint)size, 0);
        }

        size -= FirstGeneFrame.MaxGeneLength;
        var numberOfGenes = (int)(size / FollowingGeneFrame.MaxGeneLength);
        var lastGeneSize = (uint)(size - (numberOfGenes * FollowingGeneFrame.MaxGeneLength));
        return (lastGeneSize > 0 ? numberOfGenes + 2 : numberOfGenes + 1, FirstGeneFrame.MaxGeneLength, lastGeneSize);
    }
}
