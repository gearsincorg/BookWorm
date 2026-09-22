using System.Text.Json;
using Bookworm.Core.Brain;
using Bookworm.Core.Brain.Tools;
using Bookworm.Core.Library;
using Bookworm.Core.Library.Models;
using Bookworm.Core.Memory;
using Bookworm.Core.Orchestration;
using Moq;
using Xunit;

namespace Bookworm.Core.Tests;

public class LibrarianOrchestratorTests
{
    private static Mock<IVaLibraryClient> MakeClientMock()
    {
        var client = new Mock<IVaLibraryClient>();
        client.Setup(c => c.GetBookshelfAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new BookshelfSnapshot());
        client.Setup(c => c.GetHistoryAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<HistoryEntry>)[]);
        return client;
    }

    [Fact]
    public async Task ProcessUserUtteranceAsync_ReturnsTextOnly_WhenNoToolCalls()
    {
        var client = MakeClientMock();
        var brain = new StubBrain();
        brain.EnqueueText("Mostly ancient history and archaeology, based on what's on your bookshelf.");

        var orchestrator = new LibrarianOrchestrator(brain, new ToolCallExecutor(client.Object), client.Object, new InMemoryMemoryStore());
        await orchestrator.InitializeAsync();

        var response = await orchestrator.ProcessUserUtteranceAsync("what have I been reading lately?");

        Assert.Equal("Mostly ancient history and archaeology, based on what's on your bookshelf.", response);
    }

    [Fact]
    public async Task ProcessUserUtteranceAsync_ExecutesToolCall_ThenReturnsFinalText()
    {
        var client = MakeClientMock();
        client.Setup(c => c.AddToBookshelfAsync("R123", "DAISY_Audio_Human", LibraryItemType.Book, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var brain = new StubBrain();
        brain.EnqueueToolUse("t1", "add_to_bookshelf", JsonDocument.Parse("""{"bookshareId":"R123","format":"DAISY_Audio_Human"}""").RootElement);
        brain.EnqueueText("Done — added to your bookshelf.");

        var orchestrator = new LibrarianOrchestrator(brain, new ToolCallExecutor(client.Object), client.Object, new InMemoryMemoryStore());
        await orchestrator.InitializeAsync();

        var response = await orchestrator.ProcessUserUtteranceAsync("add the new Bernard Cornwell book to my bookshelf");

        Assert.Equal("Done — added to your bookshelf.", response);
        client.Verify(c => c.AddToBookshelfAsync("R123", "DAISY_Audio_Human", LibraryItemType.Book, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessUserUtteranceAsync_StopsAfterMaxRoundTrips()
    {
        var client = MakeClientMock();
        var brain = new StubBrain();
        for (var i = 0; i < 10; i++)
        {
            brain.EnqueueToolUse($"t{i}", "get_bookshelf", JsonDocument.Parse("{}").RootElement);
        }

        var orchestrator = new LibrarianOrchestrator(brain, new ToolCallExecutor(client.Object), client.Object, new InMemoryMemoryStore());
        await orchestrator.InitializeAsync();

        var response = await orchestrator.ProcessUserUtteranceAsync("loop forever");

        Assert.Contains("more steps", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Memory_PersistsAcrossOrchestratorInstances_ViaSharedStore()
    {
        var client = MakeClientMock();
        var sharedStore = new InMemoryMemoryStore();

        var brain1 = new StubBrain();
        brain1.EnqueueToolUse("t1", "remember_preference", JsonDocument.Parse("""{"note":"loves Roman history"}""").RootElement);
        brain1.EnqueueText("Got it, I'll remember that.");
        var orchestrator1 = new LibrarianOrchestrator(brain1, new ToolCallExecutor(client.Object), client.Object, sharedStore);
        await orchestrator1.InitializeAsync();
        await orchestrator1.ProcessUserUtteranceAsync("I love Roman history");
        await orchestrator1.SaveMemoryAsync();

        var brain2 = new StubBrain();
        brain2.EnqueueToolUse("t2", "recall_preferences", JsonDocument.Parse("{}").RootElement);
        brain2.EnqueueText("You've told me you love Roman history.");
        var orchestrator2 = new LibrarianOrchestrator(brain2, new ToolCallExecutor(client.Object), client.Object, sharedStore);
        await orchestrator2.InitializeAsync();
        var response = await orchestrator2.ProcessUserUtteranceAsync("what have I told you I like?");

        Assert.Equal("You've told me you love Roman history.", response);
    }
}
