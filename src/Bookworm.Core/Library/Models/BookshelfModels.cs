using System.Text.Json.Serialization;

namespace Bookworm.Core.Library.Models;

public sealed class BookshelfBookRef
{
    [JsonPropertyName("authorBy")]
    public string AuthorBy { get; set; } = "";

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("bookshareId")]
    public string BookshareId { get; set; } = "";
}

public sealed class BookshelfPeriodicalRef
{
    [JsonPropertyName("seriesId")]
    public string SeriesId { get; set; } = "";

    [JsonPropertyName("publicationDate")]
    public string PublicationDate { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
}

public sealed class BookshelfItem
{
    /// <summary>The loan-instance id — use this for remove/download operations, not BookshareId.</summary>
    [JsonPropertyName("activeTitleId")]
    public string ActiveTitleId { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("size")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Size { get; set; }

    [JsonPropertyName("format")]
    public LibraryFormat Format { get; set; } = new();

    [JsonPropertyName("book")]
    public BookshelfBookRef? Book { get; set; }

    [JsonPropertyName("periodical")]
    public BookshelfPeriodicalRef? Periodical { get; set; }

    [JsonPropertyName("dateAdded")]
    public string DateAdded { get; set; } = "";

    [JsonPropertyName("status")]
    public ItemStatus Status { get; set; } = new();

    /// <summary>The catalog id (same id used by search results) — needed to re-find this title later.</summary>
    public string? BookshareId => Book?.BookshareId;

    public string AuthorDisplay => Book?.AuthorBy?.Replace("By ", "").Trim() ?? "";
}

public sealed class BookshelfSnapshot
{
    public const int LoanCap = 20;

    public int TotalBookAndMusicBraille { get; init; }
    public IReadOnlyList<BookshelfItem> Books { get; init; } = [];
    public IReadOnlyList<BookshelfItem> Music { get; init; } = [];
    public int TotalPeriodical { get; init; }
    public IReadOnlyList<BookshelfItem> Periodicals { get; init; } = [];

    public int RemainingLoanSlots => Math.Max(0, LoanCap - TotalBookAndMusicBraille);
    public bool HasLoanSlotAvailable => TotalBookAndMusicBraille < LoanCap;
}

internal sealed class BookshelfEnvelope
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("data")]
    public BookshelfData? Data { get; set; }
}

internal sealed class BookshelfData
{
    [JsonPropertyName("totalBookAndMusicBraille")]
    public int TotalBookAndMusicBraille { get; set; }

    [JsonPropertyName("books")]
    public List<BookshelfItem> Books { get; set; } = [];

    [JsonPropertyName("musics")]
    public List<BookshelfItem> Musics { get; set; } = [];

    [JsonPropertyName("totalPeriodical")]
    public int TotalPeriodical { get; set; }

    [JsonPropertyName("periodicals")]
    public List<BookshelfItem> Periodicals { get; set; } = [];
}
