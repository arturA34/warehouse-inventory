using Microsoft.Data.Sqlite;
using Warehouse.Storage.Sqlite;

namespace Warehouse.Tests;

public sealed class SqliteMigrationTests
{
    [Fact]
    public void Migrations_create_schema_and_repeat_run_changes_nothing()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        Migrator.Apply(connection);
        var version = Migrator.ReadVersion(connection);
        Migrator.Apply(connection);

        Assert.True(version >= 1);
        Assert.Equal(version, Migrator.ReadVersion(connection));

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('products','stock','movements','inventory_locks')";
        Assert.Equal(4L, command.ExecuteScalar());
    }
}