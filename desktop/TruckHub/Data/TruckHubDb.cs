using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace TruckHub;

/// <summary>
/// Banco local principal do TruckHub. O banco fica por jogador, na pasta
/// LocalAppData, e não depende da API para existir ou funcionar.
/// </summary>
internal sealed class TruckHubDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public string DatabasePath { get; }

    public TruckHubDb()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TruckHub");

        Directory.CreateDirectory(folder);
        DatabasePath = Path.Combine(folder, "truckhub.db");

        _connection = new SqliteConnection($"Data Source={DatabasePath};Mode=ReadWriteCreate;Cache=Shared");
        _connection.Open();

        using var pragma = _connection.CreateCommand();
        pragma.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;
PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
    }

    public SqliteConnection Connection => _connection;

    public void Dispose() => _connection.Dispose();
}
