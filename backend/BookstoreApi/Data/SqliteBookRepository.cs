using System.Data;
using System.Globalization;
using System.IO;
using BookstoreApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace BookstoreApi.Data;

// SQLite implementation of `IBookRepository`.
public sealed class SqliteBookRepository : IBookRepository
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly BookstoreSchemaMapper _schemaMapper;

    public SqliteBookRepository(
        IConfiguration configuration,
        IWebHostEnvironment env,
        BookstoreSchemaMapper schemaMapper)
    {
        // We resolve the SQLite file at runtime because the DB gets copied to the build output.
        _configuration = configuration;
        _env = env;
        _schemaMapper = schemaMapper;
    }

    private string ResolveSqliteFullPath()
    {
        var sqlitePath = _configuration["Sqlite:Path"] ?? "Bookstore.sqlite";

        if (Path.IsPathRooted(sqlitePath))
            return sqlitePath;

        var contentRootCandidate = Path.Combine(_env.ContentRootPath, sqlitePath);
        var baseDirCandidate = Path.Combine(AppContext.BaseDirectory, sqlitePath);

        var fullPath = File.Exists(contentRootCandidate) ? contentRootCandidate : baseDirCandidate;

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Bookstore database not found. Looked for '{sqlitePath}' relative to the project and build output.", fullPath);

        return fullPath;
    }

    public async Task<PagedResult<BookDto>> GetBooksAsync(
        int page,
        int pageSize,
        bool sortByTitleDescending,
        string? category,
        CancellationToken cancellationToken)
    {
        // Basic bounds checks to keep pagination predictable.
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page), "page must be >= 1");

        if (pageSize < 1 || pageSize > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "pageSize must be between 1 and 100");

        var fullPath = ResolveSqliteFullPath();
        var connectionString = $"Data Source={fullPath}";
        var mapping = await _schemaMapper.GetMappingAsync(connectionString, cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var effectiveCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();

        var totalCount = await GetTotalCountAsync(connection, mapping, effectiveCategory, cancellationToken);

        var offset = (page - 1) * pageSize;

        // Sorting is required by the assignment: by book title.
        var order = sortByTitleDescending ? "DESC" : "ASC";

        var whereClause =
            effectiveCategory is null
                ? string.Empty
                : $@"WHERE ""{mapping.CategoryColumn}"" = @category";

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

    private static async Task<int> GetTotalCountAsync(
        SqliteConnection connection,
        BookSchemaMapping mapping,
        string? category,
        CancellationToken cancellationToken)
    {
        var whereClause =
            category is null
                ? string.Empty
                : $@"WHERE ""{mapping.CategoryColumn}"" = @category";

        var sql = $@"SELECT COUNT(1) FROM ""{mapping.TableName}"" {whereClause};";
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        if (category is not null)
            cmd.Parameters.AddWithValue("@category", category);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static string GetRequiredString(SqliteDataReader reader, string columnAlias)
    {
        // The assignment says all fields are required, so we throw if a DB column is NULL.
        var ordinal = reader.GetOrdinal(columnAlias);
        if (reader.IsDBNull(ordinal))
            throw new InvalidOperationException($"Database column '{columnAlias}' is NULL but the field is required.");
        return reader.GetString(ordinal);
    }

    private static int GetRequiredInt(SqliteDataReader reader, string columnAlias)
    {
        // Parse via invariant culture to avoid issues with decimal/thousand separators.
        var ordinal = reader.GetOrdinal(columnAlias);
        if (reader.IsDBNull(ordinal))
            throw new InvalidOperationException($"Database column '{columnAlias}' is NULL but the field is required.");

        var raw = reader.GetValue(ordinal).ToString() ?? throw new InvalidOperationException($"Database column '{columnAlias}' is empty.");
        return int.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static decimal GetRequiredDecimal(SqliteDataReader reader, string columnAlias)
    {
        // Parse via invariant culture to keep decimals consistent regardless of machine locale.
        var ordinal = reader.GetOrdinal(columnAlias);
        if (reader.IsDBNull(ordinal))
            throw new InvalidOperationException($"Database column '{columnAlias}' is NULL but the field is required.");

        var raw = reader.GetValue(ordinal).ToString() ?? throw new InvalidOperationException($"Database column '{columnAlias}' is empty.");
        return decimal.Parse(raw, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    public async Task<List<string>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        var fullPath = ResolveSqliteFullPath();
        var connectionString = $"Data Source={fullPath}";
        var mapping = await _schemaMapper.GetMappingAsync(connectionString, cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql =
            $@"SELECT DISTINCT
                    ""{mapping.CategoryColumn}"" AS Category
               FROM ""{mapping.TableName}""
               ORDER BY ""{mapping.CategoryColumn}"" ASC;";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        var categories = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
                categories.Add(reader.GetString(0));
        }

        return categories;
    }

    public async Task<BookDto?> GetBookByIsbnAsync(string isbn, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(isbn))
            throw new ArgumentException("ISBN is required.", nameof(isbn));

        var trimmed = isbn.Trim();

        var fullPath = ResolveSqliteFullPath();
        var connectionString = $"Data Source={fullPath}";
        var mapping = await _schemaMapper.GetMappingAsync(connectionString, cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

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
        cmd.Parameters.AddWithValue("@isbn", trimmed);

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
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

        return null;
    }

    public async Task CreateBookAsync(BookDto book, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);

        var fullPath = ResolveSqliteFullPath();
        var connectionString = $"Data Source={fullPath}";
        var mapping = await _schemaMapper.GetMappingAsync(connectionString, cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var existsCmd = connection.CreateCommand();
        existsCmd.CommandText =
            $@"SELECT COUNT(1) FROM ""{mapping.TableName}"" WHERE ""{mapping.IsbnColumn}"" = @isbn;";
        existsCmd.Parameters.AddWithValue("@isbn", book.Isbn);

        var exists = Convert.ToInt32(await existsCmd.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (exists > 0)
            throw new InvalidOperationException($"A book with ISBN '{book.Isbn}' already exists.");

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

        await insertCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> UpdateBookAsync(string originalIsbn, BookDto book, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (string.IsNullOrWhiteSpace(originalIsbn))
            throw new ArgumentException("ISBN is required.", nameof(originalIsbn));

        var trimmedOriginal = originalIsbn.Trim();

        var fullPath = ResolveSqliteFullPath();
        var connectionString = $"Data Source={fullPath}";
        var mapping = await _schemaMapper.GetMappingAsync(connectionString, cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        if (!string.Equals(trimmedOriginal, book.Isbn.Trim(), StringComparison.Ordinal))
        {
            await using var conflictCmd = connection.CreateCommand();
            conflictCmd.CommandText =
                $@"SELECT COUNT(1) FROM ""{mapping.TableName}"" WHERE ""{mapping.IsbnColumn}"" = @newIsbn;";
            conflictCmd.Parameters.AddWithValue("@newIsbn", book.Isbn.Trim());
            var otherCount = Convert.ToInt32(await conflictCmd.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            if (otherCount > 0)
                throw new InvalidOperationException($"A book with ISBN '{book.Isbn}' already exists.");
        }

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
        return rows > 0;
    }

    public async Task<bool> DeleteBookAsync(string isbn, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(isbn))
            throw new ArgumentException("ISBN is required.", nameof(isbn));

        var trimmed = isbn.Trim();

        var fullPath = ResolveSqliteFullPath();
        var connectionString = $"Data Source={fullPath}";
        var mapping = await _schemaMapper.GetMappingAsync(connectionString, cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var deleteCmd = connection.CreateCommand();
        deleteCmd.CommandText = $@"DELETE FROM ""{mapping.TableName}"" WHERE ""{mapping.IsbnColumn}"" = @isbn;";
        deleteCmd.Parameters.AddWithValue("@isbn", trimmed);

        var rows = await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }
}

