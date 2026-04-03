using System.Collections.Generic;
using BookstoreApi.Data;
using BookstoreApi.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

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
            return BadRequest(ex.Message);  // Return 400 Bad Request if page/size is invalid
        }
    }

    // GET /api/books/categories
    // This method returns a list of all book categories for the filter dropdown.
    [HttpGet("categories")]  // Responds to GET /api/books/categories
    public async Task<ActionResult<List<string>>> GetCategories(CancellationToken cancellationToken = default)
    {
        var categories = await _repository.GetCategoriesAsync(cancellationToken);
        return Ok(categories);  // Return 200 OK with the list
    }

    // POST /api/books
    // This method creates a new book in the database (for admin use).
    [HttpPost]  // Responds to POST requests
    public async Task<ActionResult<BookDto>> CreateBook([FromBody] BookDto? book, CancellationToken cancellationToken = default)
    {
        if (book is null)
            return BadRequest("Request body is required.");  // Check if book data was provided

        var validationError = ValidateBook(book);
        if (validationError is not null)
            return BadRequest(validationError);  // Check if book data is valid

        try
        {
            await _repository.CreateBookAsync(book, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, book);  // Return 201 Created
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);  // Return 409 Conflict if ISBN already exists
        }
    }

    // PUT /api/books/{isbn}
    // This method updates an existing book by ISBN (for admin use).
    [HttpPut("{isbn}")]  // Responds to PUT /api/books/{isbn}
    public async Task<ActionResult<BookDto>> UpdateBook(
        string isbn,  // ISBN from URL path
        [FromBody] BookDto? book,  // Book data from request body
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
                return NotFound($"No book found with ISBN '{isbn}'.");  // Return 404 if book not found

            return Ok(book);  // Return 200 OK with updated book
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);  // Return 409 if ISBN conflict
        }
    }

    // DELETE /api/books/{isbn}
    // This method deletes a book by ISBN (for admin use).
    [HttpDelete("{isbn}")]  // Responds to DELETE /api/books/{isbn}
    public async Task<IActionResult> DeleteBook(string isbn, CancellationToken cancellationToken = default)
    {
        var deleted = await _repository.DeleteBookAsync(isbn, cancellationToken);
        if (!deleted)
            return NotFound($"No book found with ISBN '{isbn}'.");  // Return 404 if not found

        return NoContent();  // Return 204 No Content (successful delete)
    }

    // GET /api/books/cart
    // This method gets the user's shopping cart from session storage.
    [HttpGet("cart")]  // Responds to GET /api/books/cart
    public ActionResult<Cart> GetCart()
    {
        // Get cart data from session (stored as JSON string)
        var cartJson = HttpContext.Session.GetString("Cart");
        if (string.IsNullOrEmpty(cartJson))
            return Ok(new Cart());  // Return empty cart if none stored

        try
        {
            // Deserialize JSON to Cart object
            var cart = JsonSerializer.Deserialize<Cart>(cartJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true  // Allow case-insensitive property names
            });
            return Ok(cart ?? new Cart());  // Return the cart or empty if deserialization failed
        }
        catch
        {
            return Ok(new Cart());  // Return empty cart if JSON is invalid
        }
    }

    // POST /api/books/cart/add
    // This method adds a book to the user's shopping cart.
    [HttpPost("cart/add")]  // Responds to POST /api/books/cart/add
    public async Task<ActionResult<Cart>> AddToCart([FromBody] AddToCartRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Isbn))
            return BadRequest("ISBN is required.");  // Check if ISBN was provided

        // Check if the book exists in the database
        var book = await _repository.GetBookByIsbnAsync(request.Isbn, cancellationToken);
        if (book == null)
            return NotFound($"No book found with ISBN '{request.Isbn}'.");  // Return 404 if book not found

        // Get current cart from session
        var cartJson = HttpContext.Session.GetString("Cart");
        Cart cart;
        if (string.IsNullOrEmpty(cartJson))
        {
            cart = new Cart();  // Create new cart if none exists
        }
        else
        {
            try
            {
                // Deserialize existing cart from JSON
                cart = JsonSerializer.Deserialize<Cart>(cartJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new Cart();
            }
            catch
            {
                cart = new Cart();  // Create new cart if deserialization fails
            }
        }

        // Add or update the item in the cart
        if (cart.Items.ContainsKey(request.Isbn))
        {
            cart.Items[request.Isbn].Quantity += 1;  // Increase quantity if already in cart
        }
        else
        {
            cart.Items[request.Isbn] = new CartItem { Book = book, Quantity = 1 };  // Add new item
        }

        // Save updated cart to session
        var updatedCartJson = JsonSerializer.Serialize(cart);
        HttpContext.Session.SetString("Cart", updatedCartJson);

        return Ok(cart);  // Return 200 OK with updated cart
    }
    }

    // PUT /api/books/cart/update
    // This method updates the quantity of an item in the cart.
    [HttpPut("cart/update")]  // Responds to PUT /api/books/cart/update
    public ActionResult<Cart> UpdateCartItem([FromBody] UpdateCartRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Isbn))
            return BadRequest("ISBN is required.");  // Check if ISBN was provided

        if (request.Quantity < 0)
            return BadRequest("Quantity cannot be negative.");  // Validate quantity

        // Get current cart from session
        var cartJson = HttpContext.Session.GetString("Cart");
        if (string.IsNullOrEmpty(cartJson))
            return NotFound("Cart is empty.");  // Return 404 if no cart

        Cart cart;
        try
        {
            // Deserialize cart from JSON
            cart = JsonSerializer.Deserialize<Cart>(cartJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new Cart();
        }
        catch
        {
            return BadRequest("Invalid cart data.");  // Return 400 if JSON is invalid
        }

        if (!cart.Items.ContainsKey(request.Isbn))
            return NotFound($"Book with ISBN '{request.Isbn}' not in cart.");  // Check if item exists

        if (request.Quantity == 0)
        {
            cart.Items.Remove(request.Isbn);  // Remove item if quantity is 0
        }
        else
        {
            cart.Items[request.Isbn].Quantity = request.Quantity;  // Update quantity
        }

        // Save updated cart to session
        var updatedCartJson = JsonSerializer.Serialize(cart);
        HttpContext.Session.SetString("Cart", updatedCartJson);

        return Ok(cart);
    }

    // DELETE /api/books/cart/{isbn}
    // This method removes an item from the cart by ISBN.
    [HttpDelete("cart/{isbn}")]  // Responds to DELETE /api/books/cart/{isbn}
    public ActionResult<Cart> RemoveFromCart(string isbn)
    {
        // Get current cart from session
        var cartJson = HttpContext.Session.GetString("Cart");
        if (string.IsNullOrEmpty(cartJson))
            return NotFound("Cart is empty.");  // Return 404 if no cart

        Cart cart;
        try
        {
            // Deserialize cart from JSON
            cart = JsonSerializer.Deserialize<Cart>(cartJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new Cart();
        }
        catch
        {
            return BadRequest("Invalid cart data.");  // Return 400 if JSON invalid
        }

        if (!cart.Items.Remove(isbn))
            return NotFound($"Book with ISBN '{isbn}' not in cart.");  // Check if item was removed

        // Save updated cart to session
        var updatedCartJson = JsonSerializer.Serialize(cart);
        HttpContext.Session.SetString("Cart", updatedCartJson);

        return Ok(cart);  // Return 200 OK with updated cart
    }

    // This private method validates book data before creating or updating.
    private static string? ValidateBook(BookDto book)
    {
        if (string.IsNullOrWhiteSpace(book.Title))
            return "Title is required.";  // Check required fields
        if (string.IsNullOrWhiteSpace(book.Author))
            return "Author is required.";
        if (string.IsNullOrWhiteSpace(book.Publisher))
            return "Publisher is required.";
        if (string.IsNullOrWhiteSpace(book.Isbn))
            return "ISBN is required.";
        if (string.IsNullOrWhiteSpace(book.Category))
            return "Category is required.";
        if (book.NumberOfPages < 1)
            return "NumberOfPages must be at least 1.";  // Validate page count
        if (book.Price < 0)
            return "Price cannot be negative.";  // Validate price

        return null;  // Return null if all validations pass
    }
}

