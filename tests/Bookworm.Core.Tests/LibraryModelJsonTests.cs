using System.Text.Json;
using System.Text.Json.Serialization;
using Bookworm.Core.Library.Models;
using Xunit;

namespace Bookworm.Core.Tests;

public class LibraryModelJsonTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void FlexibleStringConverter_ReadsStringSize()
    {
        var json = """{"size":"298.50 MB"}""";
        var item = JsonSerializer.Deserialize<TestSizeHolder>(json, Options)!;
        Assert.Equal("298.50 MB", item.Size);
    }

    [Fact]
    public void FlexibleStringConverter_ReadsNumericSizeAsString()
    {
        // VA sends "size":0 (a bare number) for periodicals instead of a string.
        var json = """{"size":0}""";
        var item = JsonSerializer.Deserialize<TestSizeHolder>(json, Options)!;
        Assert.Equal("0", item.Size);
    }

    [Fact]
    public void FlexibleListConverter_TreatsEmptyStringAsEmptyList()
    {
        // VA sends "subscriptions":"" instead of [] when there are none.
        var json = """{"subscriptions":""}""";
        var data = JsonSerializer.Deserialize<TestListHolder>(json, Options)!;
        Assert.NotNull(data.Subscriptions);
        Assert.Empty(data.Subscriptions);
    }

    [Fact]
    public void FlexibleListConverter_ReadsRealArray()
    {
        var json = """{"subscriptions":[{"activeTitleId":"1","title":"The Advertiser"}]}""";
        var data = JsonSerializer.Deserialize<TestListHolder>(json, Options)!;
        Assert.Single(data.Subscriptions!);
        Assert.Equal("The Advertiser", data.Subscriptions![0].Title);
    }

    [Fact]
    public void BookshelfSnapshot_ReportsRemainingLoanSlots()
    {
        var snapshot = new BookshelfSnapshot { TotalBookAndMusicBraille = 17 };
        Assert.Equal(3, snapshot.RemainingLoanSlots);
        Assert.True(snapshot.HasLoanSlotAvailable);
    }

    [Fact]
    public void BookshelfSnapshot_ReportsNoSlotsWhenFull()
    {
        var snapshot = new BookshelfSnapshot { TotalBookAndMusicBraille = 20 };
        Assert.Equal(0, snapshot.RemainingLoanSlots);
        Assert.False(snapshot.HasLoanSlotAvailable);
    }

    private sealed class TestSizeHolder
    {
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string Size { get; set; } = "";
    }

    private sealed class TestListHolder
    {
        [JsonConverter(typeof(FlexibleListConverter<Subscription>))]
        public List<Subscription>? Subscriptions { get; set; }
    }
}
