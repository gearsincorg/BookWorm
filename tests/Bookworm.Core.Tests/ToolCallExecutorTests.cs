using System.Text.Json;
using Bookworm.Core.Brain;
using Bookworm.Core.Brain.Tools;
using Bookworm.Core.Library;
using Bookworm.Core.Library.Models;
using Bookworm.Core.Memory;
using Moq;
using Xunit;

namespace Bookworm.Core.Tests;

public class ToolCallExecutorTests
{
    private static ContentBlock ToolUse(string name, string jsonInput) => new()
    {
        Type = "tool_use",
        Id = "toolu_test",
        Name = name,
        Input = JsonDocument.Parse(jsonInput).RootElement,
    };

    private static BookshelfSnapshot MakeSnapshot(int count) => new() { TotalBookAndMusicBraille = count };

    [Fact]
    public async Task AddToBookshelf_Succeeds_WhenSlotAvailable()
    {
        var client = new Mock<IVaLibraryClient>();
        client.Setup(c => c.GetBookshelfAsync(It.IsAny<CancellationToken>())).ReturnsAsync(MakeSnapshot(5));
        client.Setup(c => c.AddToBookshelfAsync("R123", "DAISY_Audio_Human", LibraryItemType.Book, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executor = new ToolCallExecutor(client.Object);
        var memory = new BookwormMemory();
        var result = await executor.ExecuteAsync(
            ToolUse("add_to_bookshelf", """{"bookshareId":"R123","format":"DAISY_Audio_Human","title":"Azincourt","author":"Cornwell, Bernard"}"""),
            memory);

        Assert.False(result.IsError);
        client.Verify(c => c.AddToBookshelfAsync("R123", "DAISY_Audio_Human", LibraryItemType.Book, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(memory.ReadingHistory);
        Assert.Equal("Azincourt", memory.ReadingHistory[0].Title);
        Assert.Equal("Cornwell, Bernard", memory.ReadingHistory[0].Author);
        Assert.Null(memory.ReadingHistory[0].DateRemoved);
    }

    [Fact]
    public async Task AddToBookshelf_ReturnsError_WhenLoanCapReached()
    {
        var client = new Mock<IVaLibraryClient>();
        client.Setup(c => c.GetBookshelfAsync(It.IsAny<CancellationToken>())).ReturnsAsync(MakeSnapshot(20));

        var executor = new ToolCallExecutor(client.Object);
        var result = await executor.ExecuteAsync(
            ToolUse("add_to_bookshelf", """{"bookshareId":"R123","format":"DAISY_Audio_Human","title":"Azincourt"}"""),
            new BookwormMemory());

        Assert.True(result.IsError);
        Assert.Contains("20", result.Content);
        client.Verify(c => c.AddToBookshelfAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<LibraryItemType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddToBookshelf_DryRun_DoesNotCallClient()
    {
        var client = new Mock<IVaLibraryClient>();
        client.Setup(c => c.GetBookshelfAsync(It.IsAny<CancellationToken>())).ReturnsAsync(MakeSnapshot(5));

        var executor = new ToolCallExecutor(client.Object, dryRun: true);
        var memory = new BookwormMemory();
        var result = await executor.ExecuteAsync(
            ToolUse("add_to_bookshelf", """{"bookshareId":"R123","format":"DAISY_Audio_Human","title":"Azincourt"}"""),
            memory);

        Assert.False(result.IsError);
        Assert.Contains("dryRun", result.Content);
        Assert.Empty(memory.ReadingHistory); // dry-run must not log to reading history either
        client.Verify(c => c.AddToBookshelfAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<LibraryItemType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveFromBookshelf_DryRun_DoesNotCallClient()
    {
        var client = new Mock<IVaLibraryClient>();
        var executor = new ToolCallExecutor(client.Object, dryRun: true);

        var result = await executor.ExecuteAsync(
            ToolUse("remove_from_bookshelf", """{"activeTitleId":"999"}"""),
            new BookwormMemory());

        Assert.False(result.IsError);
        client.Verify(c => c.RemoveFromBookshelfAsync(It.IsAny<string>(), It.IsAny<LibraryItemType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RememberPreference_AddsToMemory_AndRecallReturnsIt()
    {
        var client = new Mock<IVaLibraryClient>();
        var executor = new ToolCallExecutor(client.Object);
        var memory = new BookwormMemory();

        await executor.ExecuteAsync(ToolUse("remember_preference", """{"note":"prefers DAISY Audio (Human)"}"""), memory);
        Assert.Single(memory.ExplicitPreferences);

        var recall = await executor.ExecuteAsync(ToolUse("recall_preferences", "{}"), memory);
        Assert.False(recall.IsError);
        Assert.Contains("prefers DAISY Audio (Human)", recall.Content);
    }

    [Fact]
    public async Task UnknownTool_ReturnsError()
    {
        var client = new Mock<IVaLibraryClient>();
        var executor = new ToolCallExecutor(client.Object);

        var result = await executor.ExecuteAsync(ToolUse("not_a_real_tool", "{}"), new BookwormMemory());

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task MissingRequiredArgument_ReturnsError_NotThrow()
    {
        var client = new Mock<IVaLibraryClient>();
        var executor = new ToolCallExecutor(client.Object);

        var result = await executor.ExecuteAsync(ToolUse("add_to_request_list", "{}"), new BookwormMemory());

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task AddToBookshelf_FlagsNewAuthor_ThenNotAfterRecorded()
    {
        var client = new Mock<IVaLibraryClient>();
        client.Setup(c => c.GetBookshelfAsync(It.IsAny<CancellationToken>())).ReturnsAsync(MakeSnapshot(5));
        var executor = new ToolCallExecutor(client.Object);
        var memory = new BookwormMemory();

        var first = await executor.ExecuteAsync(
            ToolUse("add_to_bookshelf", """{"bookshareId":"R1","format":"DAISY_Audio_Human","title":"Azincourt","author":"Cornwell, Bernard"}"""),
            memory);
        Assert.Contains("\"authorAlreadyInPreferredAuthors\":false", first.Content);

        await executor.ExecuteAsync(ToolUse("add_preferred_author", """{"authorName":"Cornwell, Bernard","isFavorite":true}"""), memory);

        var second = await executor.ExecuteAsync(
            ToolUse("add_to_bookshelf", """{"bookshareId":"R2","format":"DAISY_Audio_Human","title":"Waterloo","author":"Cornwell, Bernard"}"""),
            memory);
        Assert.Contains("\"authorAlreadyInPreferredAuthors\":true", second.Content);
    }

    [Fact]
    public async Task RemoveFromBookshelf_MarksMatchingReadingHistoryEntryAsRemoved()
    {
        var client = new Mock<IVaLibraryClient>();
        client.Setup(c => c.GetBookshelfAsync(It.IsAny<CancellationToken>())).ReturnsAsync(MakeSnapshot(5));
        var executor = new ToolCallExecutor(client.Object);
        var memory = new BookwormMemory();

        await executor.ExecuteAsync(
            ToolUse("add_to_bookshelf", """{"bookshareId":"R1","format":"DAISY_Audio_Human","title":"Azincourt"}"""),
            memory);

        var result = await executor.ExecuteAsync(
            ToolUse("remove_from_bookshelf", """{"activeTitleId":"999","bookshareId":"R1"}"""),
            memory);

        Assert.False(result.IsError);
        Assert.NotNull(memory.ReadingHistory[0].DateRemoved);
    }

    [Fact]
    public async Task RateBook_SetsRating_FindableViaSearch()
    {
        var client = new Mock<IVaLibraryClient>();
        client.Setup(c => c.GetBookshelfAsync(It.IsAny<CancellationToken>())).ReturnsAsync(MakeSnapshot(5));
        var executor = new ToolCallExecutor(client.Object);
        var memory = new BookwormMemory();

        await executor.ExecuteAsync(
            ToolUse("add_to_bookshelf", """{"bookshareId":"R1","format":"DAISY_Audio_Human","title":"Azincourt","author":"Cornwell, Bernard"}"""),
            memory);

        var rateResult = await executor.ExecuteAsync(ToolUse("rate_book", """{"title":"Azincourt","rating":5}"""), memory);
        Assert.False(rateResult.IsError);
        Assert.Equal(5, memory.ReadingHistory[0].Rating);

        var searchResult = await executor.ExecuteAsync(ToolUse("search_reading_history", """{"query":"Cornwell"}"""), memory);
        Assert.Contains("Azincourt", searchResult.Content);
    }

    [Fact]
    public async Task RateBook_OutOfRange_ReturnsError()
    {
        var client = new Mock<IVaLibraryClient>();
        var executor = new ToolCallExecutor(client.Object);
        var memory = new BookwormMemory();
        memory.ReadingHistory.Add(new ReadingHistoryEntry { Title = "Azincourt", DateAdded = DateTimeOffset.UtcNow });

        var result = await executor.ExecuteAsync(ToolUse("rate_book", """{"title":"Azincourt","rating":9}"""), memory);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task AddPreferredGenre_DoesNotDuplicate()
    {
        var client = new Mock<IVaLibraryClient>();
        var executor = new ToolCallExecutor(client.Object);
        var memory = new BookwormMemory();

        await executor.ExecuteAsync(ToolUse("add_preferred_genre", """{"genre":"Historical Fiction"}"""), memory);
        await executor.ExecuteAsync(ToolUse("add_preferred_genre", """{"genre":"historical fiction"}"""), memory);

        Assert.Single(memory.PreferredGenres);
    }
}
