using System;
using System.IO;

namespace TransPoli;

/// <summary>
/// Ponto único de acesso à persistência local. Nesta primeira etapa ele apenas
/// garante a existência do banco. Os serviços atuais continuam funcionando;
/// a migração dos JSON acontecerá em etapas posteriores.
/// </summary>
internal sealed class LocalDataStore : IDisposable
{
    public TransPoliDb Db { get; }

    public LocalDataStore()
    {
        Db = new TransPoliDb();
        new DatabaseInitializer(Db).Initialize();
    }

    public string DatabasePath => Db.DatabasePath;

    public void Dispose() => Db.Dispose();
}
