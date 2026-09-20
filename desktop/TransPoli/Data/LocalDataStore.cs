using System;

namespace TransPoli;

internal sealed class LocalDataStore : IDisposable
{
    public TransPoliDb Db { get; }

    public LocalDataStore()
    {
        Db = new TransPoliDb();
        new DatabaseInitializer(Db).Initialize();
        LegacyDataMigration.Prepare(this);
    }

    public string DatabasePath => Db.DatabasePath;
    public void Dispose() => Db.Dispose();
}
