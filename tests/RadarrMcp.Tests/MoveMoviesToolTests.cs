using System.Text.Json;
using NSubstitute;
using RadarrMcp.Models;
using RadarrMcp.Services;
using RadarrMcp.Tools;
using Xunit;

namespace RadarrMcp.Tests;

public class MoveMoviesToolTests
{
    private readonly IRadarrClient _radarr = Substitute.For<IRadarrClient>();
    private readonly MoveMoviesTool _tool;

    public MoveMoviesToolTests()
    {
        _tool = new MoveMoviesTool(_radarr);

        _radarr.GetRootFoldersAsync(Arg.Any<CancellationToken>()).Returns(Result<List<RadarrRootFolder>>.Ok(
        [
            new RadarrRootFolder(1, "/movies/A", 0),
            new RadarrRootFolder(2, "/movies/T/", 0)
        ]));

        _radarr.GetLibraryAsync(Arg.Any<CancellationToken>()).Returns(Result<List<RadarrMovie>>.Ok(
        [
            Movie(1, "Alien", "/movies/A/Alien (1979)"),
            Movie(2, "Titanic", "/movies/T/Titanic (1997)"),
            Movie(3, "The Thing", "/movies/A/The Thing (1982)")
        ]));
    }

    [Fact]
    public async Task UnknownRootFolder_FailsAndListsAvailableFolders()
    {
        var json = await _tool.MoveMoviesAsync([1], "/movies/Z");

        var error = Error(json);
        Assert.Contains("/movies/Z", error);
        Assert.Contains("/movies/A", error);
        Assert.Contains("/movies/T/", error);
        await _radarr.DidNotReceive().MoveMoviesAsync(Arg.Any<RadarrMovieEditorMoveRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownMovieId_FailsWithoutSendingAnything()
    {
        var json = await _tool.MoveMoviesAsync([1, 42, 99], "/movies/T");

        var error = Error(json);
        Assert.Contains("42", error);
        Assert.Contains("99", error);
        await _radarr.DidNotReceive().MoveMoviesAsync(Arg.Any<RadarrMovieEditorMoveRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MovieAlreadyInTarget_IsSkipped_AndNoCallWhenNothingLeft()
    {
        var json = await _tool.MoveMoviesAsync([2], "/movies/T");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(0, root.GetProperty("moved").GetArrayLength());
        var skipped = root.GetProperty("skipped");
        Assert.Equal(1, skipped.GetArrayLength());
        Assert.Equal(2, skipped[0].GetProperty("id").GetInt32());
        await _radarr.DidNotReceive().MoveMoviesAsync(Arg.Any<RadarrMovieEditorMoveRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DryRun_ReturnsPlan_AndDoesNotCallRadarr()
    {
        var json = await _tool.MoveMoviesAsync([1, 2], "/movies/T", dryRun: true);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("dryRun").GetBoolean());
        Assert.False(root.TryGetProperty("moved", out _));

        var planned = root.GetProperty("planned");
        Assert.Equal(1, planned.GetArrayLength());
        Assert.Equal(1, planned[0].GetProperty("id").GetInt32());
        Assert.Equal("Alien", planned[0].GetProperty("title").GetString());
        Assert.Equal("/movies/A/Alien (1979)", planned[0].GetProperty("oldPath").GetString());
        Assert.Equal("/movies/T/Alien (1979)", planned[0].GetProperty("newPath").GetString());
        Assert.Equal(2, root.GetProperty("skipped")[0].GetProperty("id").GetInt32());

        await _radarr.DidNotReceive().MoveMoviesAsync(Arg.Any<RadarrMovieEditorMoveRequest>(), Arg.Any<CancellationToken>());
        await _radarr.DidNotReceive().GetCommandsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RealMove_SendsRemainingIds_AndReportsQueuedCommands()
    {
        var oldCommand = Command(10, "BulkMoveMovie", "completed", """{"movies":[{"movieId":1}]}""");
        _radarr.GetCommandsAsync(Arg.Any<CancellationToken>()).Returns(
            Result<List<RadarrCommandStatus>>.Ok([oldCommand]),
            Result<List<RadarrCommandStatus>>.Ok(
            [
                oldCommand,
                Command(11, "BulkMoveMovie", "queued", """{"destinationRootFolder":"/movies/T/","movies":[{"movieId":1,"sourcePath":"/movies/A/Alien (1979)"},{"movieId":3}]}"""),
                Command(12, "RssSync", "queued", "{}"),
                Command(13, "RefreshMovie", "started", """{"movieIds":[99]}""")
            ]));

        RadarrMovieEditorMoveRequest? sent = null;
        _radarr.MoveMoviesAsync(Arg.Do<RadarrMovieEditorMoveRequest>(r => sent = r), Arg.Any<CancellationToken>())
            .Returns(Result<List<RadarrMovie>>.Ok(
            [
                Movie(1, "Alien", "/movies/T/Alien (1979)"),
                Movie(3, "The Thing", "/movies/T/Thing, The (1982)")
            ]));

        var json = await _tool.MoveMoviesAsync([1, 2, 3], "/movies/T");

        Assert.NotNull(sent);
        Assert.Equal([1, 3], sent!.MovieIds);
        Assert.Equal("/movies/T/", sent.RootFolderPath); // Radarr's own spelling of the root folder
        Assert.True(sent.MoveFiles);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var moved = root.GetProperty("moved");
        Assert.Equal(2, moved.GetArrayLength());
        Assert.Equal("/movies/A/The Thing (1982)", moved[1].GetProperty("oldPath").GetString());
        Assert.Equal("/movies/T/Thing, The (1982)", moved[1].GetProperty("newPath").GetString());
        Assert.Equal(2, root.GetProperty("skipped")[0].GetProperty("id").GetInt32());

        var commands = root.GetProperty("queuedCommands");
        Assert.Equal(1, commands.GetArrayLength());
        Assert.Equal(11, commands[0].GetProperty("id").GetInt32());
        Assert.Equal("BulkMoveMovie", commands[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task MoveFilesFalse_DoesNotQueryCommands()
    {
        _radarr.MoveMoviesAsync(Arg.Any<RadarrMovieEditorMoveRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<List<RadarrMovie>>.Ok([Movie(1, "Alien", "/movies/T/Alien (1979)")]));

        var json = await _tool.MoveMoviesAsync([1], "/movies/T", moveFiles: false);

        await _radarr.Received(1).MoveMoviesAsync(
            Arg.Is<RadarrMovieEditorMoveRequest>(r => !r.MoveFiles), Arg.Any<CancellationToken>());
        await _radarr.DidNotReceive().GetCommandsAsync(Arg.Any<CancellationToken>());
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("queuedCommands", out _));
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 0 })]
    [InlineData(new[] { -5 })]
    public async Task InvalidIds_AreRejected(int[] ids)
    {
        var json = await _tool.MoveMoviesAsync(ids, "/movies/T");

        Error(json);
        await _radarr.DidNotReceive().GetLibraryAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MoreThan100Ids_AreRejected()
    {
        var json = await _tool.MoveMoviesAsync(Enumerable.Range(1, 101).ToArray(), "/movies/T");

        Assert.Contains("100", Error(json));
    }

    private static string Error(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("Error", out var error), $"Expected an error, got: {json}");
        return error.GetString()!;
    }

    private static RadarrCommandStatus Command(int id, string name, string status, string body)
    {
        using var doc = JsonDocument.Parse(body);
        return new RadarrCommandStatus(id, name, status, null, doc.RootElement.Clone());
    }

    private static RadarrMovie Movie(int id, string title, string path) => new(
        Id: id, TmdbId: id * 100, ImdbId: null, Title: title, OriginalTitle: null, TitleSlug: null,
        SortTitle: null, Year: 2000, Overview: null, Genres: null, Runtime: 0, Studio: null,
        Status: null, Monitored: true, HasFile: true, SizeOnDisk: 0, QualityProfileId: 1,
        RootFolderPath: null, Path: path, Added: null, InCinemas: null, PhysicalRelease: null,
        DigitalRelease: null, Images: null, MovieFile: null, Ratings: null);
}
