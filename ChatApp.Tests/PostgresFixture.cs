using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ChatApp.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ChatApp.Tests;
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string name = "chatapp_test_" + Guid.NewGuid().ToString("N");
    private string admin = "";
    public string? InitialMigration { get; init; }
    public string ConnectionString { get; private set; } = "";
    public AppDbContext Open() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(ConnectionString).UseSnakeCaseNamingConvention().Options);
    public async Task InitializeAsync()
    {
        admin = Environment.GetEnvironmentVariable("CHATAPP_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Set CHATAPP_TEST_POSTGRES to an isolated PostgreSQL admin connection. Tests create/drop their own chatapp_test_* database.");
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE {name}", connection).ExecuteNonQueryAsync();
        var builder = new NpgsqlConnectionStringBuilder(admin) { Database = name, Pooling = false };
        ConnectionString = builder.ConnectionString;
        await using var db = Open();
        await db.GetService<IMigrator>().MigrateAsync(InitialMigration);
    }
    public async Task DisposeAsync()
    {
        if (ConnectionString.Length == 0) return;
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS {name} WITH (FORCE)", connection).ExecuteNonQueryAsync();
    }
}
