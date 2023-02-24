﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace LP.Block;

public static class BlockPool
{
    public const int MaxPool = 100;

    static BlockPool()
    {
        pool = new ByteArrayPool(BlockService.MaxBlockSize, MaxPool);
        pool.SetMaxPool(BlockService.StandardBlockSize, BlockService.StandardBlockPool);
    }

    public static ByteArrayPool.Owner Rent(int minimumLength) => pool.Rent(minimumLength);

    public static void Dump(ILog logger) => pool.Dump(logger);

    private static ByteArrayPool pool;
}
