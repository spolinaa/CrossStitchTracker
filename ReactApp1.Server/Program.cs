using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using PatternTracker.Server.Data;
using PatternTracker.Server.Interfaces;
using PatternTracker.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Путь к SQLite делаем абсолютным от папки сервера, чтобы база не зависела
// от текущей рабочей директории запуска (иначе схемы «пропадают»).
var connectionString = builder.Configuration.GetConnectionString("TrackerDb") ?? "Data Source=tracker.db";
const string prefix = "Data Source=";
if (connectionString.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
{
    var source = connectionString[prefix.Length..].Trim();
    if (!Path.IsPathRooted(source))
        connectionString = $"{prefix}{Path.Combine(builder.Environment.ContentRootPath, source)}";
}

// Add services to the container.
builder.Services.AddDbContext<TrackerDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddScoped<IAuthorizeService, AuthorizeService>();
builder.Services.AddScoped<IPatternService, PatternService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddSingleton<ParseJobService>();
builder.Services.AddSingleton<FlossPaletteService>();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Создаем SQLite-файл и таблицы при первом запуске (без миграций — для MVP).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();
    db.Database.EnsureCreated();

    // Легкие миграции вручную: новые nullable-колонки поверх старой базы.
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    try
    {
        foreach (var (table, column, ddl) in new[]
                 {
                     ("Patterns", "SourceFileName", "TEXT"),
                     ("PatternCells", "BgColor", "TEXT"),
                     ("PatternColors", "FlossBrand", "TEXT"),
                     ("PatternColors", "FlossCode", "TEXT"),
                     ("PatternColors", "FlossHex", "TEXT"),
                     ("Patterns", "GridCols", "INTEGER"),
                     ("Patterns", "GridRows", "INTEGER")
                 })
        {
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
            var hasColumn = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
            if (!hasColumn)
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {ddl}";
                await alterCmd.ExecuteNonQueryAsync();
            }
        }
    }
    finally
    {
        await conn.CloseAsync();
    }
}

app.MapDefaultEndpoints();

app.UseDefaultFiles();
app.UseStaticFiles();

// Шрифты-символы схем: wwwroot/static_fonts/{patternId}/
var fontsRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "static_fonts");
Directory.CreateDirectory(fontsRoot);
var fontContentTypes = new FileExtensionContentTypeProvider();
fontContentTypes.Mappings[".ttf"] = "font/ttf";
fontContentTypes.Mappings[".otf"] = "font/otf";
fontContentTypes.Mappings[".woff"] = "font/woff";
fontContentTypes.Mappings[".woff2"] = "font/woff2";
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(fontsRoot),
    RequestPath = "/static_fonts",
    ContentTypeProvider = fontContentTypes
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("/index.html");

app.Run();
