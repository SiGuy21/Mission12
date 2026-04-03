// Holds the mapping from "assignment field names" to actual SQLite column names.
// This allows the code to work with different database schemas.
namespace BookstoreApi.Models;

public sealed class BookSchemaMapping
{
    // The name of the table containing book data.
    public required string TableName { get; init; }

    // Column names for book properties
    // The column name for the book title.
    public required string TitleColumn { get; init; }
    // The column name for the book author.
    public required string AuthorColumn { get; init; }
    // The column name for the book publisher.
    public required string PublisherColumn { get; init; }
    // The column name for the book ISBN.
    public required string IsbnColumn { get; init; }
    // The column name for the book category.
    public required string CategoryColumn { get; init; }
    // The column name for the number of pages.
    public required string NumberOfPagesColumn { get; init; }
    // The column name for the book price.
    public required string PriceColumn { get; init; }
}

