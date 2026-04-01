using System.Collections.Generic;
using BookstoreApi.Models;

namespace BookstoreApi.Data;

// Data-access abstraction for listing books from Bookstore.sqlite.
public interface IBookRepository
{
    // Returns a single page of books.
    // sortByTitleDescending determines whether ORDER BY Title is ASC or DESC.
    Task<PagedResult<BookDto>> GetBooksAsync(
        int page,
        int pageSize,
        bool sortByTitleDescending,
        string? category,
        CancellationToken cancellationToken);

    // Returns distinct Category values for the catalog filter.
    Task<List<string>> GetCategoriesAsync(CancellationToken cancellationToken);

    // Creates a new book row. Throws InvalidOperationException if the ISBN already exists.
    Task CreateBookAsync(BookDto book, CancellationToken cancellationToken);

    // Updates the book identified by `originalIsbn`. Returns false if no row matched.
    // Throws InvalidOperationException if the new ISBN conflicts with a different row.
    Task<bool> UpdateBookAsync(string originalIsbn, BookDto book, CancellationToken cancellationToken);

    // Deletes the book with the given ISBN. Returns false if no row matched.
    Task<bool> DeleteBookAsync(string isbn, CancellationToken cancellationToken);
}

