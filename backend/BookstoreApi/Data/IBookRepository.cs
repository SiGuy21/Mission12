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
}

