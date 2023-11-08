// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;

namespace Stormancer.Networking.Reliable;

/// <summary>
/// The <see cref="MemoryPool{T}"/> used by the connection.
/// </summary>
public interface IMemoryPoolFeature
{
    /// <summary>
    /// Gets the <see cref="MemoryPool{T}"/> used by the connection.
    /// </summary>
    MemoryPool<byte> MemoryPool { get; }
}

internal class DefaultMemoryPoolFeature(MemoryPool<byte> memoryPool) : IMemoryPoolFeature
{
    public MemoryPool<byte> MemoryPool { get; } = memoryPool;
}
