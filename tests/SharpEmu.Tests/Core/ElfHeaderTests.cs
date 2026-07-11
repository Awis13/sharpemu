// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Runtime.InteropServices;
using SharpEmu.Core.Loader;
using Xunit;

namespace SharpEmu.Tests.Core;

public sealed class ElfHeaderTests
{
    private static ElfHeader Materialize(byte[] bytes) => MemoryMarshal.Read<ElfHeader>(bytes);

    /// <summary>
    /// Builds the 64-byte ELF64 header of a minimal PS5 image, mirroring the
    /// layout produced by tools/synthetic/gen_min_elf.py.
    /// </summary>
    private static byte[] BuildValidHeader(
        byte abiVersion = 2,
        ulong entryPoint = 0x100,
        ushort phEntrySize = 56,
        ushort phCount = 1)
    {
        var bytes = new byte[64];
        bytes[0] = 0x7F;
        bytes[1] = (byte)'E';
        bytes[2] = (byte)'L';
        bytes[3] = (byte)'F';
        bytes[4] = 2;  // ELFCLASS64
        bytes[5] = 1;  // little-endian
        bytes[6] = 1;  // EV_CURRENT
        bytes[7] = 9;  // OSABI FreeBSD
        bytes[8] = abiVersion;
        BitConverter.GetBytes((ushort)2).CopyTo(bytes, 16);          // e_type ET_EXEC
        BitConverter.GetBytes((ushort)62).CopyTo(bytes, 18);         // e_machine EM_X86_64
        BitConverter.GetBytes((uint)1).CopyTo(bytes, 20);            // e_version
        BitConverter.GetBytes(entryPoint).CopyTo(bytes, 24);         // e_entry
        BitConverter.GetBytes((ulong)64).CopyTo(bytes, 32);          // e_phoff
        BitConverter.GetBytes((ushort)64).CopyTo(bytes, 52);         // e_ehsize
        BitConverter.GetBytes(phEntrySize).CopyTo(bytes, 54);        // e_phentsize
        BitConverter.GetBytes(phCount).CopyTo(bytes, 56);            // e_phnum
        return bytes;
    }

    [Fact]
    public void HasElfMagic_ReturnsTrueForValidHeader()
    {
        var header = Materialize(BuildValidHeader());
        Assert.True(header.HasElfMagic);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void HasElfMagic_ReturnsFalseWhenAnyMagicByteIsWrong(int corruptIndex)
    {
        var bytes = BuildValidHeader();
        bytes[corruptIndex] ^= 0xFF;
        var header = Materialize(bytes);
        Assert.False(header.HasElfMagic);
    }

    [Fact]
    public void Is64Bit_ReturnsFalseForElf32Class()
    {
        var bytes = BuildValidHeader();
        bytes[4] = 1; // ELFCLASS32
        var header = Materialize(bytes);
        Assert.False(header.Is64Bit);
    }

    [Fact]
    public void IsLittleEndian_ReturnsFalseForBigEndianIdent()
    {
        var bytes = BuildValidHeader();
        bytes[5] = 2; // ELFDATA2MSB
        var header = Materialize(bytes);
        Assert.False(header.IsLittleEndian);
    }

    [Fact]
    public void AbiVersion_ReadsIdent8()
    {
        Assert.Equal(2, Materialize(BuildValidHeader(abiVersion: 2)).AbiVersion);
        Assert.Equal(1, Materialize(BuildValidHeader(abiVersion: 1)).AbiVersion);
    }

    [Fact]
    public void ScalarFields_RoundTripThroughLayout()
    {
        var header = Materialize(BuildValidHeader(entryPoint: 0xDEAD_BEEF, phEntrySize: 56, phCount: 3));
        Assert.Equal(0xDEAD_BEEFUL, header.EntryPoint);
        Assert.Equal(56, header.ProgramHeaderEntrySize);
        Assert.Equal(3, header.ProgramHeaderCount);
        Assert.Equal(2, header.Type);
        Assert.Equal(62, header.Machine);
        Assert.Equal(9, header.Abi);
        Assert.Equal(64UL, header.ProgramHeaderOffset);
    }

    [Fact]
    public void StructSize_IsExactly64Bytes()
    {
        // SelfLoader materializes the header straight from image bytes; any
        // padding drift would silently corrupt every field after it.
        Assert.Equal(64, Marshal.SizeOf<ElfHeader>());
    }
}
