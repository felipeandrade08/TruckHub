using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace TransPoli;

/// <summary>
/// Banco local principal do TransPoli. O banco fica por jogador, na pasta
/// LocalAppData, e não depende da API para existir ou funcionar.
/// </summary>
internal sealed class TransPoliDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public string DatabasePath { get; }

    public TransPoliDb()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TransPoli");

        Directory.CreateDirectory(folder);
        DatabasePath = Path.Combine(folder, "transpoli.db");

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
