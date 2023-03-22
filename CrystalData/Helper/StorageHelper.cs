﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace CrystalData;

public static class StorageHelper
{
    public const long Megabytes = 1024 * 1024;
    public const long Gigabytes = 1024 * 1024 * 1024;

    public static string ByteToString(long size)
    {
        // MaxValue = 9_223_372_036_854_775_807
        // B, K, M, G, T, P, E

        if (size < 1000)
        {
            return $"{size}B";
        }

        size /= 100;
        if (size < 100)
        {
            return $"{size / 10}.{size % 10}K";
        }

        size /= 10;
        if (size < 1000)
        {
            return $"{size}K";
        }

        size /= 100;
        if (size < 100)
        {
            return $"{size / 10}.{size % 10}M";
        }

        size /= 10;
        if (size < 1000)
        {
            return $"{size}M";
        }

        size /= 100;
        if (size < 100)
        {
            return $"{size / 10}.{size % 10}G";
        }

        size /= 10;
        if (size < 1000)
        {
            return $"{size}G";
        }

        size /= 100;
        if (size < 100)
        {
            return $"{size / 10}.{size % 10}T";
        }

        size /= 10;
        if (size < 1000)
        {
            return $"{size}T";
        }

        size /= 100;
        if (size < 100)
        {
            return $"{size / 10}.{size % 10}P";
        }

        size /= 10;
        if (size < 1000)
        {
            return $"{size}P";
        }

        size /= 100;
        if (size < 100)
        {
            return $"{size / 10}.{size % 10}E";
        }

        size /= 10;
        return $"{size}E";
    }
}
