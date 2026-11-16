using Microsoft.Data.Sqlite;
using System;

namespace TransPoli;

/// <summary>
/// Registra débitos e créditos confirmados pela telemetria SCS no livro-caixa local.
/// IDs determinísticos tornam a gravação idempotente após reconexões/replays.
/// </summary>
internal sealed class EconomyLedgerRepository
{
    private readonly TransPoliDb _db;

    public EconomyLedgerRepository(TransPoliDb db) => _db = db;

    public bool RecordConfirmedEvent(string eventId, string game, string currency,
        string type, string description, double signedAmount, DateTime occurredAtUtc,
        string? tripId)
    {
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(type) ||
            double.IsNaN(signedAmount) || double.IsInfinity(signedAmount) ||
            Math.Abs(signedAmount) < 0.000001)
            return false;

        var owner = SecureTokenStore.ReadUserId();
        if (string.IsNullOrWhiteSpace(owner)) return false;

        var normalizedGame = NormalizeGame(game);
        var normalizedCurrency = NormalizeCurrency(currency);
        var id = $"scs-{normalizedGame}-{eventId}".Replace(" ", "-");

        using var command = _db.Connection.CreateCommand();
        command.CommandText = @"
INSERT OR IGNORE INTO economy_transaction
(id,trip_id,type,description,amount,occurred_at_utc,created_at_utc,owner_user_id)
VALUES(@id,@trip,@type,@description,@amount,@occurred,@created,@owner);";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@trip", string.IsNullOrWhiteSpace(tripId) ? DBNull.Value : tripId);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@description", $"[{normalizedGame} • {normalizedCurrency}] {description}");
        command.Parameters.AddWithValue("@amount", Math.Round(signedAmount, 2, MidpointRounding.AwayFromZero));
        command.Parameters.AddWithValue("@occurred", occurredAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@owner", owner);
        return command.ExecuteNonQuery() == 1;
    }

    private static string NormalizeGame(string? game)
    {
        var value = (game ?? "").Trim().ToUpperInvariant();
        if (value.Contains("AMERICAN") || value == "ATS") return "ATS";
        if (value.Contains("EURO") || value == "ETS2") return "ETS2";
        return "UNKNOWN";
    }

    private static string NormalizeCurrency(string? currency)
    {
        var value = (currency ?? "").Trim().ToUpperInvariant();
        return value is "USD" or "EUR" or "GBP" or "CHF" or "PLN" or "CZK" or "HUF" or "SEK" or "NOK" or "DKK" or "TRY" or "RUB" ? value : "N/D";
    }
}
