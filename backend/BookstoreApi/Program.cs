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

static void EnsureBookstoreDatabaseExists(IConfiguration configuration, IWebHostEnvironment env)
{
    var sqlitePath = configuration["Sqlite:Path"] ?? "Bookstore.sqlite";
    if (!Path.IsPathRooted(sqlitePath))
        return;

    if (File.Exists(sqlitePath))
        return;

    var dir = Path.GetDirectoryName(sqlitePath);
    if (!string.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir);

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

EnsureBookstoreDatabaseExists(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseCors();
app.UseSession();

app.MapControllers();

app.Run();
