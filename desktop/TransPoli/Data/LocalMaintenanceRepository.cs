using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace TransPoli;

internal sealed class LocalMaintenanceRepository
{
    private readonly TransPoliDb _db;
    public LocalMaintenanceRepository(TransPoliDb db) => _db=db;

    public void Add(string id,string? truckId,string type,string component,string description,decimal cost,double odometerKm,DateTime atUtc,string? tripId)
    {
        using var tx=_db.Connection.BeginTransaction();
        using var c=_db.Connection.CreateCommand();
        c.Transaction=tx;
        c.CommandText=@"INSERT OR IGNORE INTO maintenance
(id,truck_id,type,description,cost,odometer_km,recorded_at_utc,component,trip_id)
VALUES(@id,@truck,@type,@description,@cost,@odo,@at,@component,@trip);";
        Add(c,"@id",id);Add(c,"@truck",truckId);Add(c,"@type",type);Add(c,"@description",description);
        Add(c,"@cost",cost);Add(c,"@odo",odometerKm);Add(c,"@at",atUtc.ToUniversalTime().ToString("O"));
        Add(c,"@component",component);Add(c,"@trip",tripId);
        c.ExecuteNonQuery();

        if(cost>0)
        {
            using var e=_db.Connection.CreateCommand();
            e.Transaction=tx;
            e.CommandText=@"INSERT OR IGNORE INTO economy_transaction
(id,trip_id,type,description,amount,occurred_at_utc,created_at_utc)
VALUES(@id,@trip,'maintenance_expense',@description,@amount,@at,@created);";
            Add(e,"@id","maintenance-"+id);Add(e,"@trip",tripId);
            Add(e,"@description",$"Manutenção • {type} • {component}");
            Add(e,"@amount",-Math.Abs(cost));Add(e,"@at",atUtc.ToUniversalTime().ToString("O"));Add(e,"@created",DateTime.UtcNow.ToString("O"));
            e.ExecuteNonQuery();
        }

        if(!string.IsNullOrWhiteSpace(tripId))
        {
            using var u=_db.Connection.CreateCommand();
            u.Transaction=tx;
            u.CommandText=@"UPDATE trip SET
expense_total=COALESCE((SELECT -SUM(CASE WHEN amount<0 THEN amount ELSE 0 END) FROM economy_transaction WHERE trip_id=@trip),0),
net_value=COALESCE((SELECT SUM(amount) FROM economy_transaction WHERE trip_id=@trip),0),
updated_at_utc=@at WHERE id=@trip;";
            Add(u,"@trip",tripId);Add(u,"@at",DateTime.UtcNow.ToString("O"));u.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static void Add(SqliteCommand c,string n,object? v)=>c.Parameters.AddWithValue(n,v??DBNull.Value);
}
