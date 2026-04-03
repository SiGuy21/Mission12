// Program entry point for the BookstoreApi ASP.NET Core app.
// Wires up controllers, CORS for React dev, and dependency injection for the SQLite repository.
using BookstoreApi.Data;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    // Cross-site SPA (Azure Static Web Apps → App Service API) needs SameSite=None + Secure.
    // Local Vite dev: localhost ports are same-site; Lax works with credentials.
    if (builder.Environment.IsDevelopment())
    {
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    }
    else
    {
        options.Cookie.SameSite = SameSiteMode.None;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    }
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                "https://purple-river-04dd26810.1.azurestaticapps.net",
                "https://black-glacier-0d3671a10.1.azurestaticapps.net",
                "https://happy-desert-0bffaa010.1.azurestaticapps.net",   // ← this is your live site
                "http://localhost:5173",
                "http://localhost:4173",
                "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddSingleton<BookstoreSchemaMapper>();
builder.Services.AddScoped<IBookRepository, SqliteBookRepository>();

// This method ensures the SQLite database file exists for the bookstore.
// It copies a template database if needed, so the app has initial data.
static void EnsureBookstoreDatabaseExists(IConfiguration configuration, IWebHostEnvironment env)
{
    // Get the path to the SQLite database from config, default to "Bookstore.sqlite"
    var sqlitePath = configuration["Sqlite:Path"] ?? "Bookstore.sqlite";
    if (!Path.IsPathRooted(sqlitePath))
        return;

    // If the database already exists, do nothing
    if (File.Exists(sqlitePath))
        return;

    // Create the directory if it doesn't exist
    var dir = Path.GetDirectoryName(sqlitePath);
    if (!string.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir);

    // Try to copy from possible source locations (content root or app directory)
    foreach (var src in new[]
             {
                 Path.Combine(env.ContentRootPath, "Bookstore.sqlite"),
                 Path.Combine(AppContext.BaseDirectory, "Bookstore.sqlite")
             })
    {
        if (!File.Exists(src))
            continue;

        File.Copy(src, sqlitePath, overwrite: false);
        return;
    }
}

// Call the method to set up the database before building the app
EnsureBookstoreDatabaseExists(builder.Configuration, builder.Environment);

// Build the web application
var app = builder.Build();

// Enable CORS (Cross-Origin Resource Sharing) to allow requests from the frontend
app.UseCors();
// Enable session middleware for storing cart data
app.UseSession();

// Map controller routes (like /api/books) to their handlers
app.MapControllers();

// Start the web server and listen for requests
app.Run();
