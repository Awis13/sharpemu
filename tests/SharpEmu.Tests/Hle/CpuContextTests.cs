// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using Xunit;

namespace SharpEmu.Tests.Hle;

/// <summary>In-memory ICpuMemory backed by a flat dictionary of byte cells.</summary>
internal sealed class FakeCpuMemory : ICpuMemory
{
    private readonly Dictionary<ulong, byte> _cells = new();

    public void Store(ulong address, params byte[] bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
        {
            _cells[address + (ulong)i] = bytes[i];
        }
    }

    public bool TryRead(ulong virtualAddress, Span<byte> destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            if (!_cells.TryGetValue(virtualAddress + (ulong)i, out var value))
            {
                return false;
            }

            destination[i] = value;
        }

        return true;
    }

    public bool TryWrite(ulong virtualAddress, ReadOnlySpan<byte> source)
    {
        for (var i = 0; i < source.Length; i++)
        {
            _cells[virtualAddress + (ulong)i] = source[i];
        }

        return true;
    }
}

public sealed class CpuContextTests
{
    private static CpuContext CreateContext(out FakeCpuMemory memory)
    {
        memory = new FakeCpuMemory();
        return new CpuContext(memory, Generation.Gen5);
    }

    [Fact]
    public void Constructor_ThrowsOnNullMemory()
    {
        Assert.Throws<ArgumentNullException>(() => new CpuContext(null!, Generation.Gen5));
    }

    [Fact]
    public void Indexer_RoundTripsAllSixteenRegisters()
    {
        var context = CreateContext(out _);
        foreach (var register in Enum.GetValues<CpuRegister>())
        {
            var value = 0x1000UL + (ulong)register;
            context[register] = value;
            Assert.Equal(value, context[register]);
        }
    }

    [Fact]
    public void WasRaxWritten_SetOnlyByRaxWrites()
    {
        var context = CreateContext(out _);
        context[CpuRegister.Rdi] = 1;
        context[CpuRegister.R15] = 2;
        Assert.False(context.WasRaxWritten);

        context[CpuRegister.Rax] = 0; // even writing zero counts as an explicit rax write
        Assert.True(context.WasRaxWritten);
    }

    [Fact]
    public void ClearRaxWriteFlag_ResetsTheFlag()
    {
        var context = CreateContext(out _);
        context[CpuRegister.Rax] = 42;
        context.ClearRaxWriteFlag();
        Assert.False(context.WasRaxWritten);
        Assert.Equal(42UL, context[CpuRegister.Rax]); // value survives the flag reset
    }

    [Fact]
    public void XmmRegister_RoundTripsLowAndHighLanes()
    {
        var context = CreateContext(out _);
        context.SetXmmRegister(7, 0x1111_2222_3333_4444, 0x5555_6666_7777_8888);
        context.GetXmmRegister(7, out var low, out var high);
        Assert.Equal(0x1111_2222_3333_4444UL, low);
        Assert.Equal(0x5555_6666_7777_8888UL, high);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    public void XmmRegister_ThrowsOutsideValidIndexRange(int index)
    {
        var context = CreateContext(out _);
        Assert.Throws<ArgumentOutOfRangeException>(() => context.SetXmmRegister(index, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.GetXmmRegister(index, out _, out _));
    }

    [Fact]
    public void TryReadByte_ReadsMappedMemoryAndFailsOnUnmapped()
    {
        var context = CreateContext(out var memory);
        memory.Store(0x8000_0000, 0xAB);

        Assert.True(context.TryReadByte(0x8000_0000, out var value));
        Assert.Equal(0xAB, value);
        Assert.False(context.TryReadByte(0x9000_0000, out _));
    }

    [Fact]
    public void TryReadUInt16_AssemblesLittleEndianValue()
    {
        var context = CreateContext(out var memory);
        memory.Store(0x8000_0000, 0x34, 0x12);

        Assert.True(context.TryReadUInt16(0x8000_0000, out var value));
        Assert.Equal(0x1234, value);
    }
}
