using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Warehouse.Storage.Sqlite;

internal static class Migrator
{
    private const string Folder = "Migrations.";

    public static void Apply(SqliteConnection connection)
    {
        var current = ReadVersion(connection);
        var assembly = Assembly.GetExecutingAssembly();
        var migrations = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.Ordinal))
            .Select(name => (Name: name, Version: ParseVersion(name)))
            .OrderBy(item => item.Version)
            .ToArray();

        foreach (var (name, version) in migrations.Where(item => item.Version > current))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var sql = reader.ReadToEnd();

            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
            command.CommandText = $"PRAGMA user_version = {version.ToString(CultureInfo.InvariantCulture)}";
            command.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    public static int ReadVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static int ParseVersion(string resourceName)
    {
        var file = resourceName[(resourceName.IndexOf(Folder, StringComparison.Ordinal) + Folder.Length)..];
        return int.Parse(file[..file.IndexOf('_', StringComparison.Ordinal)], CultureInfo.InvariantCulture);
    }
}