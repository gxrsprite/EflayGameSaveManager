using EflayGameSaveManager.Core.Services;

namespace EflayGameSaveManager.Core.Tests;

public sealed class CloudPathTests
{
    [Theory]
    [InlineData("/game-save-manager/", "game-save-manager")]
    [InlineData("game-save-manager\\games", "game-save-manager/games")]
    [InlineData("", "")]
    public void NormalizeRootPath_ProducesS3FriendlySegments(string input, string expected)
    {
        Assert.Equal(expected, CloudStoragePathHelper.NormalizeRootPath(input));
    }

    [Fact]
    public void CombineKey_SkipsEmptySegments()
    {
        var key = CloudStoragePathHelper.CombineKey("/game-save-manager/", "", "games", "My Game");

        Assert.Equal("game-save-manager/games/My Game", key);
    }

    [Fact]
    public void SanitizeSegment_ReplacesInvalidPathCharacters()
    {
        var segment = CloudStoragePathHelper.SanitizeSegment("A/B:C*D?");

        Assert.Equal("A-B-C-D", segment);
    }
}
