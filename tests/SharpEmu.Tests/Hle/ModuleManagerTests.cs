// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Reflection;
using SharpEmu.HLE;
using Xunit;

namespace SharpEmu.Tests.Hle;

/// <summary>
/// HLE exports used as registration fodder. NIDs are fabricated — ModuleManager
/// keys on the attribute value, not on the real Sony hash.
/// </summary>
internal static class TestExports
{
    public const string ReturnSevenNid = "TESTret7AAAA";
    public const string SetsRaxNid = "TESTsetRaxBB";
    public const string Gen4OnlyNid = "TESTgen4CCCC";

    [SysAbiExport(Nid = ReturnSevenNid, ExportName = "test_return_seven", Target = Generation.Gen4 | Generation.Gen5)]
    public static int ReturnSeven(CpuContext context) => 7;

    [SysAbiExport(Nid = SetsRaxNid, ExportName = "test_sets_rax", Target = Generation.Gen4 | Generation.Gen5)]
    public static int SetsRax(CpuContext context)
    {
        context[CpuRegister.Rax] = 42;
        return 0;
    }

    [SysAbiExport(Nid = Gen4OnlyNid, ExportName = "test_gen4_only", Target = Generation.Gen4)]
    public static int Gen4Only(CpuContext context) => 0;
}

public sealed class ModuleManagerTests
{
    private static ModuleManager CreateRegistered()
    {
        var manager = new ModuleManager();
        manager.RegisterFromAssembly(Assembly.GetExecutingAssembly(), Generation.Gen4 | Generation.Gen5);
        return manager;
    }

    private static CpuContext CreateGen5Context() => new(new FakeCpuMemory(), Generation.Gen5);

    [Fact]
    public void RegisterFromAssembly_FindsAttributedExports()
    {
        var manager = CreateRegistered();

        Assert.True(manager.TryGetExport(TestExports.ReturnSevenNid, out var export));
        Assert.Equal("test_return_seven", export.Name);
        Assert.True(manager.TryGetExportByName("test_sets_rax", out _));
    }

    [Fact]
    public void RegisterFromAssembly_SkipsDuplicateNidsOnSecondPass()
    {
        var manager = new ModuleManager();
        var first = manager.RegisterFromAssembly(Assembly.GetExecutingAssembly(), Generation.Gen4 | Generation.Gen5);
        var second = manager.RegisterFromAssembly(Assembly.GetExecutingAssembly(), Generation.Gen4 | Generation.Gen5);

        Assert.True(first >= 3);
        Assert.Equal(0, second);
    }

    [Fact]
    public void Dispatch_UnknownNid_ReturnsNotFoundAndWritesRax()
    {
        var manager = CreateRegistered();
        var context = CreateGen5Context();

        var result = manager.Dispatch("NOSUCHnid000", context);

        Assert.Equal(OrbisGen2Result.ORBIS_GEN2_ERROR_NOT_FOUND, result);
        Assert.Equal(unchecked((ulong)(int)OrbisGen2Result.ORBIS_GEN2_ERROR_NOT_FOUND), context[CpuRegister.Rax]);
    }

    [Fact]
    public void Dispatch_GenerationMismatch_ReturnsNotImplemented()
    {
        var manager = CreateRegistered();
        var context = CreateGen5Context(); // export targets Gen4 only

        var result = manager.Dispatch(TestExports.Gen4OnlyNid, context);

        Assert.Equal(OrbisGen2Result.ORBIS_GEN2_ERROR_NOT_IMPLEMENTED, result);
    }

    [Fact]
    public void Dispatch_HandlerReturnValue_BecomesRaxWhenHandlerDidNotWriteRax()
    {
        var manager = CreateRegistered();
        var context = CreateGen5Context();

        manager.Dispatch(TestExports.ReturnSevenNid, context);

        Assert.Equal(7UL, context[CpuRegister.Rax]);
    }

    [Fact]
    public void Dispatch_ExplicitRaxWrite_IsNotOverwrittenByReturnValue()
    {
        var manager = CreateRegistered();
        var context = CreateGen5Context();

        manager.Dispatch(TestExports.SetsRaxNid, context);

        Assert.Equal(42UL, context[CpuRegister.Rax]); // handler returned 0 but wrote rax=42
    }

    [Fact]
    public void Freeze_RejectsFurtherRegistration()
    {
        var manager = CreateRegistered();
        manager.Freeze();

        Assert.Throws<InvalidOperationException>(
            () => manager.RegisterFromAssembly(Assembly.GetExecutingAssembly(), Generation.Gen5));
    }

    [Fact]
    public void TryDispatch_ThrowsOnNullContextAndEmptyNid()
    {
        var manager = CreateRegistered();

        Assert.Throws<ArgumentNullException>(() => manager.TryDispatch(TestExports.ReturnSevenNid, null!, out _));
        Assert.Throws<ArgumentException>(() => manager.TryDispatch(" ", CreateGen5Context(), out _));
    }
}
