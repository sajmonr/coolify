using Npgsql;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var postgresConnectionString =
    builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Missing 'Postgres' connection string.");
var redisConnectionString =
    builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Missing 'Redis' connection string.");

builder.Services.AddSingleton(NpgsqlDataSource.Create(postgresConnectionString));
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConnectionString)
);

var app = builder.Build();

// Ensure the database and schema exist before the app starts serving.
await EnsureDatabaseExistsAsync(postgresConnectionString);
await using (var schema = app.Services.GetRequiredService<NpgsqlDataSource>().CreateCommand(
    """
    CREATE TABLE IF NOT EXISTS visits (
        id         bigserial PRIMARY KEY,
        visited_at timestamptz NOT NULL DEFAULT now()
    );
    """))
{
    await schema.ExecuteNonQueryAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet(
    "/",
    async (NpgsqlDataSource db, IConnectionMultiplexer redis) =>
    {
        // --- Exercise PostgreSQL: record this visit and read back the total. ---
        await using (var insert = db.CreateCommand("INSERT INTO visits DEFAULT VALUES;"))
        {
            await insert.ExecuteNonQueryAsync();
        }

        long dbVisitCount;
        await using (var count = db.CreateCommand("SELECT count(*) FROM visits;"))
        {
            dbVisitCount = (long)(await count.ExecuteScalarAsync())!;
        }

        // --- Exercise Redis: increment a counter and read it back. ---
        var redisDb = redis.GetDatabase();
        var redisHitCount = await redisDb.StringIncrementAsync("coolifytest:hits");

        return Results.Ok(
            new
            {
                status = "ok",
                message = "Database and Redis are both reachable.",
                db = new { visitCount = dbVisitCount },
                redis = new { hitCount = redisHitCount },
            }
        );
    }
);

await app.RunAsync();

// Creates the target database if it does not already exist. PostgreSQL has no
// "CREATE DATABASE IF NOT EXISTS", so we connect to the maintenance database,
// check the catalog, and create it only when missing.
static async Task EnsureDatabaseExistsAsync(string connectionString)
{
    var builder = new NpgsqlConnectionStringBuilder(connectionString);
    var targetDatabase = builder.Database
        ?? throw new InvalidOperationException("Postgres connection string has no database.");

    builder.Database = "postgres";

    await using var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);

    await using (var exists = dataSource.CreateCommand("SELECT 1 FROM pg_database WHERE datname = $1;"))
    {
        exists.Parameters.AddWithValue(targetDatabase);
        if (await exists.ExecuteScalarAsync() is not null)
        {
            return;
        }
    }

    // Database name can't be parameterized; quote it to guard against injection.
    var quoted = "\"" + targetDatabase.Replace("\"", "\"\"") + "\"";
    await using var create = dataSource.CreateCommand($"CREATE DATABASE {quoted};");
    await create.ExecuteNonQueryAsync();
}
