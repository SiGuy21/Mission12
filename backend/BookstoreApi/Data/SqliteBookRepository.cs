using System.Data;
using System.Globalization;
using System.IO;
using BookstoreApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace BookstoreApi.Data;

// This class implements IBookRepository using SQLite database.
// It handles all database operations for books and cart data.
public sealed class SqliteBookRepository : IBookRepository
{
    private readonly IConfiguration _configuration;  // Access to app settings
    private readonly IWebHostEnvironment _env;  // Info about the web environment
    private readonly BookstoreSchemaMapper _schemaMapper;  // Maps database schema

    // Constructor: injects dependencies
    public SqliteBookRepository(
        IConfiguration configuration,
        IWebHostEnvironment env,
        BookstoreSchemaMapper schemaMapper)
    {
        _configuration = configuration;
        _env = env;
        _schemaMapper = schemaMapper;
    }

    // This method finds the full path to the SQLite database file.
    // It checks config and looks in content root or build output directory.
    private string ResolveSqliteFullPath()
    {
        var sqlitePath = _configuration["Sqlite:Path"] ?? "Bookstore.sqlite";  // Get path from config

        if (Path.IsPathRooted(sqlitePath))
            return sqlitePath;  // If absolute path, use it

        // Try relative to content root or build directory
        var contentRootCandidate = Path.Combine(_env.ContentRootPath, sqlitePath);
        var baseDirCandidate = Path.Combine(AppContext.BaseDirectory, sqlitePath);

        var fullPath = File.Exists(contentRootCandidate) ? contentRootCandidate : baseDirCandidate;

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Bookstore database not found. Looked for '{sqlitePath}' relative to the project and build output.", fullPath);

        return fullPath;  // Return the found path
    }

    // Implements IBookRepository.GetBooksAsync
    // Gets a page of books from the database with sorting and filtering.
    public async Task<PagedResult<BookDto>> GetBooksAsync(
        int page,
        int pageSize,
        bool sortByTitleDescending,
        string? category,
        CancellationToken cancellationToken)
    {
        // Validate input parameters
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page), "page must be >= 1");

        if (pageSize < 1 || pageSize > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "pageSize must be between 1 and 100");

        // Get the database file path and connection string
        var fullPath = ResolveSqliteFullPath();
        var connectionString = $"Data Source={fullPath}";
        // Get the database schema mapping (column names)
        var mapping = await _schemaMapper.GetMappingAsync(connectionString, cancellationToken);

        // Open database connection
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Clean up category filter
        var effectiveCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();

        // Get total count of books for pagination
        var totalCount = await GetTotalCountAsync(connection, mapping, effectiveCategory, cancellationToken);

        // Calculate offset for SQL LIMIT/OFFSET
        var offset = (page - 1) * pageSize;

        // Determine sort order
        var order = sortByTitleDescending ? "DESC" : "ASC";

        // Build WHERE clause for category filter
        var whereClause =
            effectiveCategory is null
                ? string.Empty
                : $@"WHERE ""{mapping.CategoryColumn}"" = @category";

        // Build SQL query to select books with pagination and sorting
        var sql =
            $@"SELECT
                    ""{mapping.TitleColumn}"" AS Title,
                    ""{mapping.AuthorColumn}"" AS Author,
                    ""{mapping.PublisherColumn}"" AS Publisher,
                    ""{mapping.IsbnColumn}"" AS Isbn,
                    ""{mapping.CategoryColumn}"" AS Category,
                    ""{mapping.NumberOfPagesColumn}"" AS NumberOfPages,
                    ""{mapping.PriceColumn}"" AS Price
                FROM ""{mapping.TableName}""
                {whereClause}
                ORDER BY ""{mapping.TitleColumn}"" {order}
                LIMIT @pageSize OFFSET @offset;";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@pageSize", pageSize);
        cmd.Parameters.AddWithValue("@offset", offset);
        if (effectiveCategory is not null)
            cmd.Parameters.AddWithValue("@category", effectiveCategory);

        var items = new List<BookDto>(pageSize);

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // Map the aliased columns (Title, Author, etc.) into the BookDto shape expected by React.
            items.Add(new BookDto
            {
                Title = GetRequiredString(reader, "Title"),
                Author = GetRequiredString(reader, "Author"),
                Publisher = GetRequiredString(reader, "Publisher"),
                Isbn = GetRequiredString(reader, "Isbn"),
                Category = GetRequiredString(reader, "Category"),
                NumberOfPages = GetRequiredInt(reader, "NumberOfPages"),
                Price = GetRequiredDecimal(reader, "Price")
            });
        }

        return new PagedResult<BookDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = items
        };
    }

    // Helper method to get the total count of books for pagination.
    private static async Task<int> GetTotalCountAsync(
        SqliteConnection connection,
        BookSchemaMapping mapping,
        string? category,
        CancellationToken cancellationToken)
    {
        // Build WHERE clause for category filter
        var whereClause =
            category is null
                ? string.Empty
                : $@"WHERE ""{mapping.CategoryColumn}"" = @category";

        // SQL to count total rows
        var sql = $@"SELECT COUNT(1) FROM ""{mapping.TableName}"" {whereClause};";
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        if (category is not null)
            cmd.Parameters.AddWithValue("@category", category);  // Add parameter
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);  // Convert to int
    }

    // Helper method to get a required string from the database reader.
    private static string GetRequiredString(SqliteDataReader reader, string columnAlias)
    {
        // The assignment says all fields are required, so we throw if a DB column is NULL.
        var ordinal = reader.GetOrdinal(columnAlias);
        if (reader.IsDBNull(ordinal))
            throw new InvalidOperationException($"Database column '{columnAlias}' is NULL but the field is required.");
        return reader.GetString(ordinal);
    }

    // Helper method to get a required int from the database reader.
    private static int GetRequiredInt(SqliteDataReader reader, string columnAlias)
    {
        // Parse via invariant culture to avoid issues with decimal/thousand separators.
        var ordinal = reader.GetOrdinal(columnAlias);
        if (reader.IsDBNull(ordinal))
            throw new InvalidOperationException($"Database column '{columnAlias}' is NULL but the field is required.");

        var raw = reader.GetValue(ordinal).ToString() ?? throw new InvalidOperationException($"Database column '{columnAlias}' is empty.");
        return int.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    // Helper method to get a required decimal from the database reader.
    private static decimal GetRequiredDecimal(SqliteDataReader reader, string columnAlias)
    {
        // Parse via invariant culture to keep decimals consistent regardless of machine locale.
        var ordinal = reader.GetOrdinal(columnAlias);
        if (reader.IsDBNull(ordinal))
            throw new InvalidOperationException($"Database column '{columnAlias}' is NULL but the field is required.");

        var raw = reader.GetValue(ordinal).ToString() ?? throw new InvalidOperationException($"Database column '{columnAlias}' is empty.");
        return decimal.Parse(raw, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    // Implements IBookRepository.GetCategoriesAsync
    // Gets all unique category names from the database.
    public async Task<List<string>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        // Get database path and schema mapping
        var fullPath = ResolveSqliteFullPath();
        var connectionString = $"Data Source={fullPath}";
        var mapping = await _schemaMapper.GetMappingAsync(connectionString, cancellationToken);

        // Open database connection
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // SQL to get distinct categories, sorted
        var sql =
            $@"SELECT DISTINCT
                    ""{mapping.CategoryColumn}"" AS Category
               FROM ""{mapping.TableName}""
               ORDER BY ""{mapping.CategoryColumn}"" ASC;";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        var categories = new List<string>();
        // Execute query and collect results
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))  // Only add non-null categories
                categories.Add(reader.GetString(0));
        }

        return categories;
    }

    // Implements IBookRepository.GetBookByIsbnAsync
    // Finds a single book by ISBN.
    public async Task<BookDto?> GetBookByIsbnAsync(string isbn, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(isbn))
            throw new ArgumentException("ISBN is required.", nameof(isbn));  // Validate input

        var trimmed = isbn.Trim();  // Clean up ISBN

        // Get database path and schema
        var fullPath = ResolveSqliteFullPath();
        var connectionString = $"Data Source={fullPath}";
        var mapping = await _schemaMapper.GetMappingAsync(connectionString, cancellationToken);

        // Open connection
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Build SQL to find book by ISBN
        var sql =
            $@"SELECT
                    ""{mapping.TitleColumn}"" AS Title,
                    ""{mapping.AuthorColumn}"" AS Author,
                    ""{mapping.PublisherColumn}"" AS Publisher,
                    ""{mapping.IsbnColumn}"" AS Isbn,
                    ""{mapping.CategoryColumn}"" AS Category,
                    ""{mapping.NumberOfPagesColumn}"" AS NumberOfPages,
                    ""{mapping.PriceColumn}"" AS Price
                FROM ""{mapping.TableName}""
                WHERE ""{mapping.IsbnColumn}"" = @isbn;";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@isbn", trimmed);  // Add ISBN parameter to prevent SQL injection

        // Execute query and read result
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            // Build BookDto from database row
            return new BookDto
            {
                Title = GetRequiredString(reader, "Title"),
                Author = GetRequiredString(reader, "Author"),
                Publisher = GetRequiredString(reader, "Publisher"),
                Isbn = GetRequiredString(reader, "Isbn"),
                Category = GetRequiredString(reader, "Category"),
                NumberOfPages = GetRequiredInt(reader, "NumberOfPages"),
                Price = GetRequiredDecimal(reader, "Price")
            };
        }

        return null;  // Return null if no book found with this ISBN
    }

    // Implements IBookRepository.CreateBookAsync
    // Adds a new book to the database.
    public async Task CreateBookAsync(BookDto book, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);  // Validate input

        // Get database path and schema
        var fullPath = ResolveSqliteFullPath();
        var connectionString = $"Data Source={fullPath}";
        var mapping = await _schemaMapper.GetMappingAsync(connectionString, cancellationToken);

        // Open connection
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Check if book already exists
        await using var existsCmd = connection.CreateCommand();
        existsCmd.CommandText =
            $@"SELECT COUNT(1) FROM ""{mapping.TableName}"" WHERE ""{mapping.IsbnColumn}"" = @isbn;";
        existsCmd.Parameters.AddWithValue("@isbn", book.Isbn);

        var exists = Convert.ToInt32(await existsCmd.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (exists > 0)
            throw new InvalidOperationException($"A book with ISBN '{book.Isbn}' already exists.");  // Prevent duplicates

        // Insert the new book
        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText =
            $@"INSERT INTO ""{mapping.TableName}"" (
                    ""{mapping.TitleColumn}"",
                    ""{mapping.AuthorColumn}"",
                    ""{mapping.PublisherColumn}"",
                    ""{mapping.IsbnColumn}"",
                    ""{mapping.CategoryColumn}"",
                    ""{mapping.NumberOfPagesColumn}"",
                    ""{mapping.PriceColumn}""
                ) VALUES (
                    @title, @author, @publisher, @isbn, @category, @pages, @price
                );";

        insertCmd.Parameters.AddWithValue("@title", book.Title);
        insertCmd.Parameters.AddWithValue("@author", book.Author);
        insertCmd.Parameters.AddWithValue("@publisher", book.Publisher);
        insertCmd.Parameters.AddWithValue("@isbn", book.Isbn);
        insertCmd.Parameters.AddWithValue("@category", book.Category);
        insertCmd.Parameters.AddWithValue("@pages", book.NumberOfPages);
        insertCmd.Parameters.AddWithValue("@price", book.Price);

        await insertCmd.ExecuteNonQueryAsync(cancellationToken);  // Execute insert
    }

    // Implements IBookRepository.UpdateBookAsync
    // Updates an existing book in the database.
    public async Task<bool> UpdateBookAsync(string originalIsbn, BookDto book, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (string.IsNullOrWhiteSpace(originalIsbn))
            throw new ArgumentException("ISBN is required.", nameof(originalIsbn));  // Validate input

        var trimmedOriginal = originalIsbn.Trim();  // Clean up ISBN

        // Get database path and schema
        var fullPath = ResolveSqliteFullPath();
        var connectionString = $"Data Source={fullPath}";
        var mapping = await _schemaMapper.GetMappingAsync(connectionString, cancellationToken);

        // Open connection
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Check for ISBN conflicts if ISBN is changing
        if (!string.Equals(trimmedOriginal, book.Isbn.Trim(), StringComparison.Ordinal))
        {
            await using var conflictCmd = connection.CreateCommand();
            conflictCmd.CommandText =
                $@"SELECT COUNT(1) FROM ""{mapping.TableName}"" WHERE ""{mapping.IsbnColumn}"" = @newIsbn;";
            conflictCmd.Parameters.AddWithValue("@newIsbn", book.Isbn.Trim());
            var otherCount = Convert.ToInt32(await conflictCmd.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            if (otherCount > 0)
                throw new InvalidOperationException($"A book with ISBN '{book.Isbn}' already exists.");  // Prevent conflicts
        }

        // Update the book
        await using var updateCmd = connection.CreateCommand();
        updateCmd.CommandText =
            $@"UPDATE ""{mapping.TableName}"" SET
                    ""{mapping.TitleColumn}"" = @title,
                    ""{mapping.AuthorColumn}"" = @author,
                    ""{mapping.PublisherColumn}"" = @publisher,
                    ""{mapping.IsbnColumn}"" = @isbn,
                    ""{mapping.CategoryColumn}"" = @category,
                    ""{mapping.NumberOfPagesColumn}"" = @pages,
                    ""{mapping.PriceColumn}"" = @price
                WHERE ""{mapping.IsbnColumn}"" = @originalIsbn;";

        updateCmd.Parameters.AddWithValue("@title", book.Title);
        updateCmd.Parameters.AddWithValue("@author", book.Author);
        updateCmd.Parameters.AddWithValue("@publisher", book.Publisher);
        updateCmd.Parameters.AddWithValue("@isbn", book.Isbn);
        updateCmd.Parameters.AddWithValue("@category", book.Category);
        updateCmd.Parameters.AddWithValue("@pages", book.NumberOfPages);
        updateCmd.Parameters.AddWithValue("@price", book.Price);
        updateCmd.Parameters.AddWithValue("@originalIsbn", trimmedOriginal);

        var rows = await updateCmd.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;  // Return true if a row was updated
    }

    // Implements IBookRepository.DeleteBookAsync
    // Removes a book from the database by ISBN.
    public async Task<bool> DeleteBookAsync(string isbn, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(isbn))
            throw new ArgumentException("ISBN is required.", nameof(isbn));  // Validate input

        var trimmed = isbn.Trim();  // Clean up ISBN

        // Get database path and schema
        var fullPath = ResolveSqliteFullPath();
        var connectionString = $"Data Source={fullPath}";
        var mapping = await _schemaMapper.GetMappingAsync(connectionString, cancellationToken);

        // Open connection
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Delete the book
        await using var deleteCmd = connection.CreateCommand();
        deleteCmd.CommandText = $@"DELETE FROM ""{mapping.TableName}"" WHERE ""{mapping.IsbnColumn}"" = @isbn;";
        deleteCmd.Parameters.AddWithValue("@isbn", trimmed);

        var rows = await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;  // Return true if a row was deleted
    }
}

