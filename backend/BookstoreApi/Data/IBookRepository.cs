using System.Collections.Generic;
using BookstoreApi.Models;

namespace BookstoreApi.Data;

// This interface defines the contract for accessing book data.
// It abstracts the database operations so the controller doesn't need to know about SQLite.
public interface IBookRepository
{
    // Gets a page of books from the database, with sorting and optional category filtering.
    // page: which page to get (1-based)
    // pageSize: how many books per page
    // sortByTitleDescending: true for Z-A, false for A-Z
    // category: filter by category, or null for all
    Task<PagedResult<BookDto>> GetBooksAsync(
        int page,
        int pageSize,
        bool sortByTitleDescending,
        string? category,
        CancellationToken cancellationToken);

    // Gets all unique category names from the books table.
    Task<List<string>> GetCategoriesAsync(CancellationToken cancellationToken);

    // Finds a single book by its ISBN, returns null if not found.
    Task<BookDto?> GetBookByIsbnAsync(string isbn, CancellationToken cancellationToken);

    // Adds a new book to the database. Throws exception if ISBN already exists.
    Task CreateBookAsync(BookDto book, CancellationToken cancellationToken);

    // Updates an existing book by its original ISBN. Returns true if updated, false if not found.
    // Throws exception if new ISBN conflicts with another book.
    Task<bool> UpdateBookAsync(string originalIsbn, BookDto book, CancellationToken cancellationToken);

    // Deletes a book by ISBN. Returns true if deleted, false if not found.
    Task<bool> DeleteBookAsync(string isbn, CancellationToken cancellationToken);
}

