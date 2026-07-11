// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using Xunit;

namespace SharpEmu.Tests.Hle;

public sealed class AerolibTests
{
    // Known-good pairs: the NIDs KernelExports registers under, cross-checked
    // against scripts/generate_aerolib_binary.py (SHA1(name+salt) reversed).
    [Theory]
    [InlineData("hcuQgD53UxM", "printf")]
    [InlineData("uMei1W9uyNo", "exit")]
    public void TryGetName_ResolvesKnownKernelNids(string nid, string expectedName)
    {
        Assert.True(Aerolib.Instance.TryGetName(nid, out var name));
        Assert.Equal(expectedName, name);
    }

    [Fact]
    public void GetName_ReturnsInputForUnknownNid()
    {
        Assert.Equal("NOSUCHnid000", Aerolib.Instance.GetName("NOSUCHnid000"));
    }

    [Fact]
    public void GetName_ReturnsEmptyStringForNull()
    {
        Assert.Equal(string.Empty, Aerolib.Instance.GetName(null!));
    }

    [Fact]
    public void ContainsNid_FalseForEmptyAndUnknown()
    {
        Assert.False(Aerolib.Instance.ContainsNid(""));
        Assert.False(Aerolib.Instance.ContainsNid("NOSUCHnid000"));
        Assert.True(Aerolib.Instance.ContainsNid("hcuQgD53UxM"));
    }

    [Fact]
    public void Instance_LoadsEmbeddedCatalog()
    {
        // The embedded aerolib.bin ships ~148k symbols; a sane lower bound
        // catches an accidentally truncated or unparsed resource.
        Assert.True(Aerolib.Instance.GetAllNidNames().Count > 100_000);
    }

    [Fact]
    public void EmptyCatalog_ResolvesNothing()
    {
        Assert.False(Aerolib.Empty.TryGetByNid("hcuQgD53UxM", out _));
        Assert.False(Aerolib.Empty.TryGetByExportName("printf", out _));
    }

    [Fact]
    public void TryGetByExportName_ResolvesReverseDirection()
    {
        Assert.True(((ISymbolCatalog)Aerolib.Instance).TryGetByExportName("printf", out var symbol));
        Assert.Equal("hcuQgD53UxM", symbol.Nid);
    }
}
