namespace BookstoreApi.Models;

// Common pagination wrapper returned by the API.
// `totalCount` is used by React to calculate how many pages exist.
public sealed class PagedResult<T>
{
    // The current page number (1-based).
    public required int Page { get; init; }
    // The number of items per page.
    public required int PageSize { get; init; }
    // The total number of items across all pages.
    public required int TotalCount { get; init; }
    // The items on this page.
    public required IReadOnlyList<T> Items { get; init; }
}

