using System.Text.Json.Serialization;

namespace Bookworm.Core.Library.Models;

// NOTE: item shapes (fields on RequestListItem/Subscription themselves) are best-effort based on the
// same rendering module as the confirmed bookshelf/search endpoints (library_features_dodp) — the
// account tested against had 0 items in both lists, so the container shape is confirmed live but the
// per-item field names are not. Validate against a real non-empty list during further Phase 1 testing
// (see docs/va-endpoints.md).

public sealed class RequestListItem
{
    [JsonPropertyName("activeTitleId")]
    public string ActiveTitleId { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("format")]
    public LibraryFormat? Format { get; set; }

    [JsonPropertyName("book")]
    public BookshelfBookRef? Book { get; set; }

    [JsonPropertyName("dateAdded")]
    public string DateAdded { get; set; } = "";

    public string? BookshareId => Book?.BookshareId;
    public string AuthorDisplay => Book?.AuthorBy?.Replace("By ", "").Trim() ?? "";

    [JsonExtensionData]
    public Dictionary<string, object?>? Extra { get; set; }
}

internal sealed class RequestListEnvelope
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("data")]
    public RequestListData? Data { get; set; }
}

/// <summary>Confirmed live: field is "requestList" (not "books"), "totalRequestList" gives the count.</summary>
internal sealed class RequestListData
{
    [JsonPropertyName("requestList")]
    [JsonConverter(typeof(FlexibleListConverter<RequestListItem>))]
    public List<RequestListItem>? RequestList { get; set; }

    [JsonPropertyName("totalRequestList")]
    public int TotalRequestList { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object?>? Extra { get; set; }
}

public sealed class Subscription
{
    [JsonPropertyName("activeTitleId")]
    public string ActiveTitleId { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("format")]
    public LibraryFormat? Format { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object?>? Extra { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("data")]
    public SubscriptionData? Data { get; set; }
}

/// <summary>Confirmed live: "subscriptions" is "" (empty string) rather than [] when there are none —
/// see <see cref="FlexibleListConverter{T}"/>. "totalSubscription" gives the count.</summary>
internal sealed class SubscriptionData
{
    [JsonPropertyName("subscriptions")]
    [JsonConverter(typeof(FlexibleListConverter<Subscription>))]
    public List<Subscription>? Subscriptions { get; set; }

    [JsonPropertyName("totalSubscription")]
    public int TotalSubscription { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object?>? Extra { get; set; }
}

public sealed class HistoryEntry
{
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public string DateLoaned { get; init; } = "";
}

public sealed class LibraryPreferences
{
    public string? DefaultFormat { get; init; }
    public string? NarratorType { get; init; }
    public string? NarratorGender { get; init; }
    public string? Language { get; init; }
    public bool AutoSendToBookshelf { get; init; }
    public int AutoSendMaxTitles { get; init; } = 20;
}
