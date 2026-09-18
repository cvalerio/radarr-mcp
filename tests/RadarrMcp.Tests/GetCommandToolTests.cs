using System.Text.Json;
using NSubstitute;
using RadarrMcp.Models;
using RadarrMcp.Services;
using RadarrMcp.Tools;
using Xunit;

namespace RadarrMcp.Tests;

public class GetCommandToolTests
{
    private readonly IRadarrClient _radarr = Substitute.For<IRadarrClient>();
    private readonly GetCommandTool _tool;

    public GetCommandToolTests() => _tool = new GetCommandTool(_radarr);

    [Fact]
    public async Task CompletedCommand_ReturnsCompactStatus()
    {
        var started = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);
        _radarr.GetCommandAsync(11, Arg.Any<CancellationToken>()).Returns(Result<RadarrCommandStatus?>.Ok(
            new RadarrCommandStatus(11, "BulkMoveMovie", "completed", started.AddSeconds(-1), null,
                Started: started, Ended: started.AddSeconds(95), Duration: "00:01:35.2000000",
                Message: "Moving Alien from '/movies/A/Alien (1979)' to '/movies/T/Alien (1979)' (1/1)",
                Result: "successful")));

        var json = await _tool.GetCommandAsync(11);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(11, root.GetProperty("id").GetInt32());
        Assert.Equal("BulkMoveMovie", root.GetProperty("name").GetString());
        Assert.Equal("completed", root.GetProperty("status").GetString());
        Assert.Equal(95.2, root.GetProperty("durationSeconds").GetDouble());
        Assert.StartsWith("Moving Alien", root.GetProperty("message").GetString());
        Assert.False(root.TryGetProperty("body", out _));
        Assert.False(root.TryGetProperty("errorMessage", out _));
    }

    [Fact]
    public async Task FailedCommand_ReturnsFirstExceptionLineAsErrorMessage()
    {
        var exception = "System.IO.IOException: There is not enough space on the disk.\n   at NzbDrone.Common.Disk...\n" + new string('x', 5000);
        _radarr.GetCommandAsync(12, Arg.Any<CancellationToken>()).Returns(Result<RadarrCommandStatus?>.Ok(
            new RadarrCommandStatus(12, "BulkMoveMovie", "failed", null, null, Message: "Failed", Exception: exception)));

        var json = await _tool.GetCommandAsync(12);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("failed", root.GetProperty("status").GetString());
        Assert.Equal("System.IO.IOException: There is not enough space on the disk.", root.GetProperty("errorMessage").GetString());
        Assert.True(root.GetProperty("exception").GetString()!.Length < exception.Length);
    }

    [Fact]
    public async Task UnknownId_ReturnsClearError()
    {
        _radarr.GetCommandAsync(999, Arg.Any<CancellationToken>()).Returns(Result<RadarrCommandStatus?>.Ok(null));

        var json = await _tool.GetCommandAsync(999);

        var error = Error(json);
        Assert.Contains("999", error);
        Assert.Contains("limited time", error);
    }

    [Fact]
    public async Task ClientError_IsReturnedAsError()
    {
        _radarr.GetCommandAsync(5, Arg.Any<CancellationToken>())
            .Returns(Result<RadarrCommandStatus?>.Fail("Cannot connect to Radarr"));

        Assert.Contains("Cannot connect", Error(await _tool.GetCommandAsync(5)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task NonPositiveId_IsRejected(int id)
    {
        Error(await _tool.GetCommandAsync(id));
        await _radarr.DidNotReceive().GetCommandAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private static string Error(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("Error", out var error), $"Expected an error, got: {json}");
        return error.GetString()!;
    }
}
