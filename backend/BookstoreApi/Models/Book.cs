namespace BookstoreApi.Models;

// Book model returned by the API.
// For this assignment, DTO and domain are the same shape so React can consume it directly.
public sealed class Book
{
    // The title of the book.
    public required string Title { get; set; }
    // The author of the book.
    public required string Author { get; set; }
    // The publisher of the book.
    public required string Publisher { get; set; }
    // The ISBN (International Standard Book Number) of the book.
    public required string Isbn { get; set; }
    // The category or genre of the book.
    public required string Category { get; set; }
    // The number of pages in the book.
    public required int NumberOfPages { get; set; }
    // The price of the book in dollars.
    public required decimal Price { get; set; }
}

