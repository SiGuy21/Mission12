using System.Collections.Generic;
using BookstoreApi.Data;
using BookstoreApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace BookstoreApi.Controllers;

// Exposes API endpoints under `/api/*` for the React frontend.
[ApiController]
[Route("api/[controller]")]
public sealed class BooksController : ControllerBase
{
    private readonly IBookRepository _repository;

    public BooksController(IBookRepository repository)
    {
        _repository = repository;
    }

    // GET /api/books?page=1&pageSize=5&sort=title&sortDir=asc|desc
    // Optional: category=Biography|Self-Help|...
    // Assignment requirement: only support sorting by title.
    [HttpGet]
    public async Task<ActionResult<PagedResult<BookDto>>> GetBooks(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 5,
        [FromQuery] string sort = "title",
        [FromQuery] string sortDir = "asc",
        [FromQuery] string? category = null,
        CancellationToken cancellationToken = default)
    {
        // Defensive validation so React can't request unsupported sorts.
        if (!string.Equals(sort, "title", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only sort='title' is supported for this assignment.");

        try
        {
            bool desc;
            if (string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase))
                desc = true;
            else if (string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase))
                desc = false;
            else
                return BadRequest("sortDir must be either 'asc' or 'desc'.");

            // Repository does the actual SQL + pagination against Bookstore.sqlite.
            var result = await _repository.GetBooksAsync(page, pageSize, desc, category, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // GET /api/books/categories
    [HttpGet("categories")]
    public async Task<ActionResult<List<string>>> GetCategories(CancellationToken cancellationToken = default)
    {
        var categories = await _repository.GetCategoriesAsync(cancellationToken);
        return Ok(categories);
    }

    // POST /api/books
    [HttpPost]
    public async Task<ActionResult<BookDto>> CreateBook([FromBody] BookDto? book, CancellationToken cancellationToken = default)
    {
        if (book is null)
            return BadRequest("Request body is required.");

        var validationError = ValidateBook(book);
        if (validationError is not null)
            return BadRequest(validationError);

        try
        {
            await _repository.CreateBookAsync(book, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, book);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    // PUT /api/books/{isbn}
    [HttpPut("{isbn}")]
    public async Task<ActionResult<BookDto>> UpdateBook(
        string isbn,
        [FromBody] BookDto? book,
        CancellationToken cancellationToken = default)
    {
        if (book is null)
            return BadRequest("Request body is required.");

        var validationError = ValidateBook(book);
        if (validationError is not null)
            return BadRequest(validationError);

        try
        {
            var updated = await _repository.UpdateBookAsync(isbn, book, cancellationToken);
            if (!updated)
                return NotFound($"No book found with ISBN '{isbn}'.");

            return Ok(book);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    // DELETE /api/books/{isbn}
    [HttpDelete("{isbn}")]
    public async Task<IActionResult> DeleteBook(string isbn, CancellationToken cancellationToken = default)
    {
        var deleted = await _repository.DeleteBookAsync(isbn, cancellationToken);
        if (!deleted)
            return NotFound($"No book found with ISBN '{isbn}'.");

        return NoContent();
    }

    private static string? ValidateBook(BookDto book)
    {
        if (string.IsNullOrWhiteSpace(book.Title))
            return "Title is required.";
        if (string.IsNullOrWhiteSpace(book.Author))
            return "Author is required.";
        if (string.IsNullOrWhiteSpace(book.Publisher))
            return "Publisher is required.";
        if (string.IsNullOrWhiteSpace(book.Isbn))
            return "ISBN is required.";
        if (string.IsNullOrWhiteSpace(book.Category))
            return "Category is required.";
        if (book.NumberOfPages < 1)
            return "NumberOfPages must be at least 1.";
        if (book.Price < 0)
            return "Price cannot be negative.";

        return null;
    }
}

