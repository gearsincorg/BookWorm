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
        var result = await executor.ExecuteAsync(
            ToolUse("add_to_bookshelf", """{"bookshareId":"R123","format":"DAISY_Audio_Human"}"""),
            new BookwormMemory());

        Assert.False(result.IsError);
        client.Verify(c => c.AddToBookshelfAsync("R123", "DAISY_Audio_Human", LibraryItemType.Book, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddToBookshelf_ReturnsError_WhenLoanCapReached()
    {
        var client = new Mock<IVaLibraryClient>();
        client.Setup(c => c.GetBookshelfAsync(It.IsAny<CancellationToken>())).ReturnsAsync(MakeSnapshot(20));

        var executor = new ToolCallExecutor(client.Object);
        var result = await executor.ExecuteAsync(
            ToolUse("add_to_bookshelf", """{"bookshareId":"R123","format":"DAISY_Audio_Human"}"""),
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
        var result = await executor.ExecuteAsync(
            ToolUse("add_to_bookshelf", """{"bookshareId":"R123","format":"DAISY_Audio_Human"}"""),
            new BookwormMemory());

        Assert.False(result.IsError);
        Assert.Contains("dryRun", result.Content);
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
}
