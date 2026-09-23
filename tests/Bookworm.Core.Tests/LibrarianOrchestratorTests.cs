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
        brain.EnqueueToolUse("t1", "add_to_bookshelf", JsonDocument.Parse("""{"bookshareId":"R123","format":"DAISY_Audio_Human","title":"Azincourt"}""").RootElement);
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
    public async Task FailedTurn_RollsBackHistory_AndDoesNotPoisonFutureTurns()
    {
        // Regression test for a real bug: a turn that throws mid-way (e.g. a Claude API error) used to
        // leave a dangling tool_use/malformed message in the shared history, so every subsequent turn
        // failed the same way for the rest of the session. It also must not leave the turn gate stuck.
        var client = MakeClientMock();
        var brain = new Mock<IBrain>();
        brain.SetupSequence(b => b.RespondAsync(It.IsAny<ConversationContext>(), It.IsAny<IReadOnlyList<ToolDefinition>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated Claude API failure"))
            .ReturnsAsync(new BrainTurnResult { Content = [ContentBlock.OfText("All good now.")] });

        var orchestrator = new LibrarianOrchestrator(brain.Object, new ToolCallExecutor(client.Object), client.Object, new InMemoryMemoryStore());
        await orchestrator.InitializeAsync();

        var countBefore = orchestrator.MessageCountForTests;
        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.ProcessUserUtteranceAsync("first attempt"));
        Assert.Equal(countBefore, orchestrator.MessageCountForTests); // rolled back, not left dangling

        var response = await orchestrator.ProcessUserUtteranceAsync("second attempt");
        Assert.Equal("All good now.", response);
    }

    [Fact]
    public async Task ConcurrentTurns_AreSerialized_NotInterleaved()
    {
        // Regression test: two turns racing on the same ConversationContext used to be possible (e.g.
        // the user speaking again before the previous reply finished), interleaving messages and
        // breaking Claude's tool_use/tool_result pairing requirement for the rest of the session.
        var client = MakeClientMock();
        var brain = new Mock<IBrain>();
        brain.Setup(b => b.RespondAsync(It.IsAny<ConversationContext>(), It.IsAny<IReadOnlyList<ToolDefinition>>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(50); // simulate a slow API call, giving a concurrent call a chance to race
                return new BrainTurnResult { Content = [ContentBlock.OfText("reply")] };
            });

        var orchestrator = new LibrarianOrchestrator(brain.Object, new ToolCallExecutor(client.Object), client.Object, new InMemoryMemoryStore());
        await orchestrator.InitializeAsync();

        var countBefore = orchestrator.MessageCountForTests;
        var first = orchestrator.ProcessUserUtteranceAsync("question one");
        var second = orchestrator.ProcessUserUtteranceAsync("question two");
        await Task.WhenAll(first, second);

        // Each turn adds exactly one user message + one assistant message; serialized execution means
        // the count is exactly 4 more, never corrupted by interleaving.
        Assert.Equal(countBefore + 4, orchestrator.MessageCountForTests);
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

    [Fact]
    public async Task SaveMemoryAsync_UsesTheEtagFromThePreviousSave_NotTheOriginalLoad()
    {
        // Regression test for a real bug: every successful save changes the store's ETag, but the
        // orchestrator kept reusing the ETag from Initialize's load on every save — so the second save
        // in any session always sent a stale precondition and failed (HTTP 412 in the real Azure store).
        var client = MakeClientMock();
        var memoryStore = new Mock<IMemoryStore>();
        memoryStore.Setup(m => m.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryLoadResult(new BookwormMemory(), "etag-from-load"));
        memoryStore.SetupSequence(m => m.SaveAsync(It.IsAny<BookwormMemory>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-after-first-save")
            .ReturnsAsync("etag-after-second-save");

        var orchestrator = new LibrarianOrchestrator(new StubBrain(), new ToolCallExecutor(client.Object), client.Object, memoryStore.Object);
        await orchestrator.InitializeAsync();

        await orchestrator.SaveMemoryAsync();
        memoryStore.Verify(m => m.SaveAsync(It.IsAny<BookwormMemory>(), "etag-from-load", It.IsAny<CancellationToken>()), Times.Once);

        await orchestrator.SaveMemoryAsync();
        memoryStore.Verify(m => m.SaveAsync(It.IsAny<BookwormMemory>(), "etag-after-first-save", It.IsAny<CancellationToken>()), Times.Once);
    }
}
