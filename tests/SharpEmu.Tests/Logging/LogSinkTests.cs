// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Logging;
using Xunit;

namespace SharpEmu.Tests.Logging;

internal sealed class RecordingSink : ISharpEmuLogSink
{
    public List<LogEntry> Entries { get; } = new();

    public void Write(in LogEntry entry) => Entries.Add(entry);
}

internal sealed class ThrowingSink : ISharpEmuLogSink
{
    public void Write(in LogEntry entry) => throw new InvalidOperationException("broken sink");
}

public sealed class LogSinkTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("sharpemu-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static LogEntry MakeEntry(string message, LogLevel level = LogLevel.Info) => new(
        Timestamp: new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero),
        Level: level,
        Category: "Tests",
        Message: message,
        SourceFileName: "LogSinkTests.cs",
        SourceLine: 1,
        SourceMemberName: nameof(MakeEntry));

    [Fact]
    public void FileLogSink_WritesLevelCategoryAndMessage()
    {
        var path = Path.Combine(_tempDir, "log.txt");
        using (var sink = new FileLogSink(path))
        {
            sink.Write(MakeEntry("hello file sink", LogLevel.Warning));
        }

        var content = File.ReadAllText(path);
        Assert.Contains("hello file sink", content);
        Assert.Contains("[Tests]", content);
        Assert.Contains("[2026-07-11", content); // timestamp prefix on by default
    }

    [Fact]
    public void FileLogSink_OmitsTimestampWhenDisabled()
    {
        var path = Path.Combine(_tempDir, "log.txt");
        using (var sink = new FileLogSink(path, includeTimestamp: false))
        {
            sink.Write(MakeEntry("no timestamp"));
        }

        Assert.DoesNotContain("[2026-07-11", File.ReadAllText(path));
    }

    [Fact]
    public void FileLogSink_AppendFalse_TruncatesExistingFile()
    {
        var path = Path.Combine(_tempDir, "log.txt");
        File.WriteAllText(path, "stale content\n");

        using (var sink = new FileLogSink(path, append: false))
        {
            sink.Write(MakeEntry("fresh"));
        }

        var content = File.ReadAllText(path);
        Assert.DoesNotContain("stale content", content);
        Assert.Contains("fresh", content);
    }

    [Fact]
    public void FileLogSink_CreatesMissingParentDirectories()
    {
        var path = Path.Combine(_tempDir, "nested", "deeper", "log.txt");
        using (var sink = new FileLogSink(path))
        {
            sink.Write(MakeEntry("created dirs"));
        }

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void FileLogSink_WriteAfterDispose_IsIgnored()
    {
        var path = Path.Combine(_tempDir, "log.txt");
        var sink = new FileLogSink(path);
        sink.Dispose();

        sink.Write(MakeEntry("after dispose")); // must not throw
        Assert.DoesNotContain("after dispose", File.ReadAllText(path));
    }

    [Fact]
    public void CompositeLogSink_FansOutToAllSinks()
    {
        var first = new RecordingSink();
        var second = new RecordingSink();
        var composite = new CompositeLogSink(first, second);

        composite.Write(MakeEntry("fan out"));

        Assert.Single(first.Entries);
        Assert.Single(second.Entries);
        Assert.Equal("fan out", first.Entries[0].Message);
    }

    [Fact]
    public void CompositeLogSink_BrokenSinkDoesNotBlockOthers()
    {
        var recording = new RecordingSink();
        var composite = new CompositeLogSink(new ThrowingSink(), recording);

        composite.Write(MakeEntry("survives"));

        Assert.Single(recording.Entries);
    }

    [Fact]
    public void CompositeLogSink_RejectsNullSinkElements()
    {
        Assert.Throws<ArgumentNullException>(() => new CompositeLogSink(new RecordingSink(), null!));
    }

    [Theory]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("INFO", LogLevel.Info)]
    [InlineData("warn", LogLevel.Warning)]
    [InlineData("Warning", LogLevel.Warning)]
    [InlineData("fatal", LogLevel.Critical)]
    [InlineData(" trace ", LogLevel.Trace)]
    public void TryParseLevel_AcceptsAliasesAndIgnoresCase(string text, LogLevel expected)
    {
        Assert.True(SharpEmuLog.TryParseLevel(text, out var level));
        Assert.Equal(expected, level);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("loud")]
    public void TryParseLevel_RejectsUnknownInput(string? text)
    {
        Assert.False(SharpEmuLog.TryParseLevel(text, out _));
    }
}
