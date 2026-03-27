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

        var sqlitePath = _configuration["Sqlite:Path"] ?? "Bookstore.sqlite";

        // The scaffold copies Bookstore.sqlite into the build output folder, so we try both:
        // 1) content root (project folder)
        // 2) base directory (bin/... where the file gets copied)
        string fullPath;
        if (Path.IsPathRooted(sqlitePath))
        {
            fullPath = sqlitePath;
        }
        else
        {
            var contentRootCandidate = Path.Combine(_env.ContentRootPath, sqlitePath);
            var baseDirCandidate = Path.Combine(AppContext.BaseDirectory, sqlitePath);

            fullPath = File.Exists(contentRootCandidate) ? contentRootCandidate : baseDirCandidate;
        }

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Bookstore database not found. Looked for '{sqlitePath}' relative to the project and build output.", fullPath);

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
        var sqlitePath = _configuration["Sqlite:Path"] ?? "Bookstore.sqlite";

        // The scaffold copies Bookstore.sqlite into the build output folder, so we try both:
        // 1) content root (project folder)
        // 2) base directory (bin/... where the file gets copied)
        string fullPath;
        if (Path.IsPathRooted(sqlitePath))
        {
            fullPath = sqlitePath;
        }
        else
        {
            var contentRootCandidate = Path.Combine(_env.ContentRootPath, sqlitePath);
            var baseDirCandidate = Path.Combine(AppContext.BaseDirectory, sqlitePath);

            fullPath = File.Exists(contentRootCandidate) ? contentRootCandidate : baseDirCandidate;
        }

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Bookstore database not found. Looked for '{sqlitePath}' relative to the project and build output.", fullPath);

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
}

