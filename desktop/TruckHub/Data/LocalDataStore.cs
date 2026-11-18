using System;

namespace TruckHub;

internal sealed class LocalDataStore : IDisposable
{
    public TruckHubDb Db { get; }

    public LocalDataStore()
    {
        Db = new TruckHubDb();
        new DatabaseInitializer(Db).Initialize();
        LegacyDataMigration.Prepare(this);
        LocalData.Current = this;
    }

    public string DatabasePath => Db.DatabasePath;
    public void Dispose() { if (ReferenceEquals(LocalData.Current, this)) LocalData.Current = null; Db.Dispose(); }
}
