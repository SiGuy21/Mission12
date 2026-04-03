using System.Data;
using System.Text.RegularExpressions;
using System.Linq;
using BookstoreApi.Models;
using Microsoft.Data.Sqlite;

namespace BookstoreApi.Data;

// Infers which SQLite table/columns correspond to our required Book fields.
// This keeps the app working even if the DB uses slightly different column names.
public sealed class BookstoreSchemaMapper
{
    // Thread-safe caching to avoid repeated schema scans.
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _cachedConnectionString;
    private BookSchemaMapping? _cachedMapping;

    // Gets the schema mapping for a database connection.
    // Caches results per connection string to improve performance.
    public async Task<BookSchemaMapping> GetMappingAsync(string connectionString, CancellationToken cancellationToken)
    {
        // Cache per connection string so we don't repeatedly re-scan schema metadata.
        if (_cachedMapping is not null && string.Equals(_cachedConnectionString, connectionString, StringComparison.OrdinalIgnoreCase))
        {
            return _cachedMapping;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedMapping is not null && string.Equals(_cachedConnectionString, connectionString, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedMapping;
            }

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            var mapping = await InferMappingAsync(connection, cancellationToken);
            _cachedConnectionString = connectionString;
            _cachedMapping = mapping;
            return mapping;
        }
        finally
        {
            _lock.Release();
        }
    }

    // Analyzes the database schema to find the best table and column matches.
    private static async Task<BookSchemaMapping> InferMappingAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // 1) list user tables
        // 2) score each table by how well its columns match required fields
        var tables = new List<string>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT name
                                FROM sqlite_master
                                WHERE type = 'table'
                                  AND name NOT LIKE 'sqlite_%'
                                ORDER BY name;";
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(reader.GetString(0));
            }
        }

        if (tables.Count == 0)
        {
            throw new InvalidOperationException("No user tables found in Bookstore.sqlite.");
        }

        BookSchemaMapping? best = null;
        var bestScore = int.MinValue;

        foreach (var table in tables)
        {
            var columns = await GetColumnNamesAsync(connection, table, cancellationToken);

            var candidates = InferColumns(table, columns, out var score);
            if (candidates is null)
                continue;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidates;
            }
        }

        if (best is null)
        {
            throw new InvalidOperationException("Could not match SQLite table/columns to required Book fields.");
        }

        return best;
    }

    // Gets all column names for a table using SQLite PRAGMA.
    private static async Task<List<string>> GetColumnNamesAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var columns = new List<string>();
        await using var cmd = connection.CreateCommand();

        // PRAGMA table_info returns schema metadata including column names.
        cmd.CommandText = $"PRAGMA table_info(\"{EscapeIdentifier(tableName)}\");";
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // PRAGMA table_info columns:
            // cid, name, type, notnull, dflt_value, pk
            columns.Add(reader.GetString(1));
        }
        return columns;
    }

    // Finds the best column matches for each book field and scores the table.
    private static BookSchemaMapping? InferColumns(string tableName, IReadOnlyList<string> columns, out int score)
    {
        score = 0;

        // Candidate columns picked via fuzzy matching of expected assignment field names.
        string? title = PickColumn(columns, c => ContainsAll(c, "title"));
        string? author = PickColumn(columns, c => ContainsAll(c, "author"));
        string? publisher = PickColumn(columns, c => ContainsAll(c, "publisher"));
        string? isbn = PickColumn(columns, c => ContainsAll(c, "isbn"));

        // The assignment says Classification/Category. Try a few reasonable column names.
        string? category =
            PickColumn(columns, c => ContainsAny(c, "category")) ??
            PickColumn(columns, c => ContainsAny(c, "classification")) ??
            PickColumn(columns, c => ContainsAny(c, "genre"));

        // The assignment calls it "Number of Pages".
        string? pages =
            PickColumn(columns, c => ContainsAny(c, "pages") || ContainsAny(c, "numberofpages") || ContainsAll(c, "number", "page")) ??
            PickColumn(columns, c => ContainsAny(c, "page"));

        string? price = PickColumn(columns, c => ContainsAny(c, "price"));

        if (title is null || author is null || publisher is null || isbn is null || category is null || pages is null || price is null)
        {
            return null;
        }

        score += 10 * ExactOrContainsScore(columns, title, "title");
        score += 10 * ExactOrContainsScore(columns, author, "author");
        score += 10 * ExactOrContainsScore(columns, publisher, "publisher");
        score += 10 * ExactOrContainsScore(columns, isbn, "isbn");
        score += 10 * ExactOrContainsScore(columns, category, "category", "classification", "genre");
        score += 10 * ExactOrContainsScore(columns, pages, "pages", "page");
        score += 10 * ExactOrContainsScore(columns, price, "price");

        return new BookSchemaMapping
        {
            TableName = tableName,
            TitleColumn = title,
            AuthorColumn = author,
            PublisherColumn = publisher,
            IsbnColumn = isbn,
            CategoryColumn = category,
            NumberOfPagesColumn = pages,
            PriceColumn = price
        };
    }

    // Selects the best matching column based on a predicate.
    private static string? PickColumn(IReadOnlyList<string> columns, Func<string, bool> predicate)
    {
        // Prefer exact-like matches first, then contains.
        var exact = columns.FirstOrDefault(c => predicate(Normalize(c)) && IsExactMatchForNormalized(Normalize(c), columns, predicate));
        if (exact is not null) return exact;

        // Otherwise, return first matching column.
        foreach (var c in columns)
        {
            if (predicate(Normalize(c)))
                return c;
        }

        return null;
    }

    // Checks if a normalized column name is an exact match for the predicate.
    private static bool IsExactMatchForNormalized(string normalizedCandidate, IReadOnlyList<string> columns, Func<string, bool> predicate)
    {
        // This is intentionally permissive; we just want to avoid selecting something totally unrelated.
        return predicate(normalizedCandidate);
    }

    // Scores how well a column matches expected keywords (exact match = 2, contains = 1).
    private static int ExactOrContainsScore(IReadOnlyList<string> columns, string? candidate, params string[] keywords)
    {
        if (candidate is null) return 0;
        var norm = Normalize(candidate);
        if (keywords.Any(k => string.Equals(norm, Normalize(k), StringComparison.OrdinalIgnoreCase))) return 2;
        if (keywords.Any(k => norm.Contains(Normalize(k), StringComparison.OrdinalIgnoreCase))) return 1;
        return 0;
    }

    // Checks if normalized column contains all required parts.
    private static bool ContainsAll(string normalizedColumn, params string[] requiredParts) =>
        requiredParts.All(p => normalizedColumn.Contains(Normalize(p), StringComparison.OrdinalIgnoreCase));

    // Checks if normalized column contains any of the parts.
    private static bool ContainsAny(string normalizedColumn, params string[] parts) =>
        parts.Any(p => normalizedColumn.Contains(Normalize(p), StringComparison.OrdinalIgnoreCase));

    // Normalizes a string by lowercasing and removing non-alphanumeric characters.
    private static string Normalize(string s)
    {
        // Lowercase and strip non-alphanumerics to make matching more forgiving.
        var lower = s.ToLowerInvariant();
        return Regex.Replace(lower, @"[^a-z0-9]+", "");
    }

    // Escapes SQLite identifiers by doubling quotes.
    private static string EscapeIdentifier(string identifier)
        => identifier.Replace("\"", "\"\"");
}

