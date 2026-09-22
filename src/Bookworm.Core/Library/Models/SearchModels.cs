using System.Text.Json.Serialization;

namespace Bookworm.Core.Library.Models;

/// <summary>Item type for add/remove bookshelf operations (the URL segment VA uses there) — a coarser
/// grouping than <see cref="VaSearchType"/>, which mirrors the search form's own dropdown values.</summary>
public enum LibraryItemType
{
    Book,
    Periodical,
    Music
}

/// <summary>Exact values from the VA search form's "Type" dropdown — used verbatim in the search request.</summary>
public static class VaSearchType
{
    public const string Book = "Book";
    public const string PictureBook = "Picture Book";
    public const string Magazine = "Magazine";
    public const string Newspaper = "Newspaper";
    public const string Podcast = "Podcast";
    public const string Music = "Music";
}

public sealed class SearchQuery
{
    public required string Keyword { get; init; }

    /// <summary>One of <see cref="VaSearchType"/>'s constants. Defaults to Book.</summary>
    public string Type { get; init; } = VaSearchType.Book;

    /// <summary>A <c>formatId</c> value (e.g. "DAISY_Audio_Human"), or empty/null for "All formats".</summary>
    public string? Format { get; init; }
}

public sealed class SearchResultItem
{
    [JsonPropertyName("activeTitleId")]
    public string ActiveTitleId { get; set; } = "";

    [JsonPropertyName("bookshareId")]
    public string BookshareId { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("size")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string Size { get; set; } = "";

    [JsonPropertyName("authors")]
    public List<LibraryAuthor> Authors { get; set; } = [];

    [JsonPropertyName("formats")]
    public List<LibraryFormat> Formats { get; set; } = [];

    [JsonPropertyName("status")]
    public ItemStatus Status { get; set; } = new();

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("seriesId")]
    public string? SeriesId { get; set; }

    public string AuthorNames => string.Join(", ", Authors.Select(a => a.DisplayName));
}

internal sealed class SearchTabResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("heading")]
    public string Heading { get; set; } = "";

    [JsonPropertyName("listArticle")]
    public List<SearchResultItem> ListArticle { get; set; } = [];
}

public sealed class SearchTabResult
{
    public int Total { get; init; }
    public string Heading { get; init; } = "";
    public IReadOnlyList<SearchResultItem> Items { get; init; } = [];

    internal static SearchTabResult From(SearchTabResponse? response) => response is null
        ? new SearchTabResult()
        : new SearchTabResult { Total = response.Total, Heading = response.Heading, Items = response.ListArticle };
}

public sealed class SearchResults
{
    public required SearchTabResult Books { get; init; }
    public required SearchTabResult Periodicals { get; init; }
    public required SearchTabResult Music { get; init; }
}

internal sealed class QuickSearchEnvelope
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("data")]
    public QuickSearchData? Data { get; set; }
}

internal sealed class QuickSearchData
{
    [JsonPropertyName("bookTab")]
    public SearchTabResponse? BookTab { get; set; }

    [JsonPropertyName("periodicalTab")]
    public SearchTabResponse? PeriodicalTab { get; set; }

    [JsonPropertyName("musicTab")]
    public SearchTabResponse? MusicTab { get; set; }
}
