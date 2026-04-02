// Program entry point for the BookstoreApi ASP.NET Core app.
// Wires up controllers, CORS for React dev, and dependency injection for the SQLite repository.
using BookstoreApi.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
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
