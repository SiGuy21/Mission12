namespace BookstoreApi.Models;

// JSON-serialized DTO returned by the API.
// Property names are emitted in camelCase so the React types can match them naturally.
public sealed class BookDto
{
    // The title of the book.
    public required string Title { get; init; }
    // The author of the book.
    public required string Author { get; init; }
    // The publisher of the book.
    public required string Publisher { get; init; }
    // The ISBN (International Standard Book Number) of the book.
    // Stored as string so ISBN formatting (dashes/leading zeros) is preserved.
    public required string Isbn { get; init; }
    // The category or genre of the book.
    public required string Category { get; init; }
    // The number of pages in the book.
    public required int NumberOfPages { get; init; }
    // The price of the book in dollars.
    public required decimal Price { get; init; }
}

