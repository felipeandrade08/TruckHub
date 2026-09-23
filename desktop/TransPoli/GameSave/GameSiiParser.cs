using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TransPoli.GameSave;

public sealed class GameSiiParser
{
    // Text SII blocks use: "type : id {" followed by fields and a closing "}".
    // The previous expression accidentally searched for literal \\r/\\n text,
    // so a valid game.sii produced zero blocks even though the build succeeded.
    private static readonly Regex BlockRegex = new(
        @"(?ms)^\s*(?<type>[A-Za-z0-9_.]+)\s*:\s*(?<id>[^\s\r\n{]+)\s*\{\s*\r?\n(?<body>.*?)^\s*\}\s*$",
        RegexOptions.Compiled);

    // Brackets are important for SII arrays such as accessories[0].
    private static readonly Regex FieldRegex = new(
        @"(?m)^\s*(?<key>[A-Za-z0-9_\[\]]+)\s*:\s*(?<value>.*?)\s*$",
        RegexOptions.Compiled);

    public IReadOnlyList<SiiBlock> ParseBlocks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<SiiBlock>();

        var blocks = new List<SiiBlock>();

        foreach (Match match in BlockRegex.Matches(text))
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match field in FieldRegex.Matches(match.Groups["body"].Value))
                fields[field.Groups["key"].Value] = field.Groups["value"].Value.Trim();

            blocks.Add(new SiiBlock(
                match.Groups["type"].Value,
                match.Groups["id"].Value,
                fields));
        }

        return blocks;
    }

    public GameSaveSnapshot ParseSnapshot(string text)
    {
        var blocks = ParseBlocks(text);

        var snapshot = new GameSaveSnapshot
        {
            ParsedAtUtc = DateTime.UtcNow,
            BlockCount = blocks.Count,
            RawTextLength = text?.Length ?? 0
        };

        var player = blocks.FirstOrDefault(b =>
            b.Type.Equals("player", StringComparison.OrdinalIgnoreCase));

        snapshot.HeadquartersCity = player?.GetCleanString("hq_city");
        snapshot.CurrentTruck = ParseCurrentTruck(player, blocks);
        snapshot.CurrentTrailer = ParseCurrentTrailer(player, blocks);
        snapshot.Trucks = ParseFleetTrucks(player, blocks);
        snapshot.Trailers = ParseFleetTrailers(player, blocks);
        snapshot.Tachograph = ParseTachograph(player);
        snapshot.DriverStats = ParseDriverStats(player);
        snapshot.TransportedCargoTypes = ParseStringArray(player, "transported_cargo_types");
        snapshot.DeliveryHistory = ParseDeliveryHistory(player, blocks);

        // TruckHub economy remains completely independent from ETS2.
        // No money, bank, revenue, price or economy field is interpreted here.
        return snapshot;
    }

    private static SaveDriverStats ParseDriverStats(SiiBlock? player)
    {
        if (player is null)
            return new SaveDriverStats();

        var cargoTypes = ParseStringArray(player, "transported_cargo_types");
        var deliveryRefs = GetArrayValues(player, "delivery_log");

        return new SaveDriverStats
        {
            DrivingMinutes = Integer(player, "driving_time"),
            MinutesSinceMandatoryBreak = Integer(player, "time_since_mandatory_break"),
            BreakMinutes = Integer(player, "on_break_time"),
            VisitedCities = Integer(player, "visited_cities"),
            ServiceVisits = Integer(player, "service_visit_count"),
            GasStationVisits = Integer(player, "gas_station_visit_count"),
            CrashCount = Integer(player, "ai_crash_count"),
            RedLightFineCount = Integer(player, "red_light_fine_count"),
            CancelledJobs = Integer(player, "cancelled_job_count"),
            TotalFuelLiters = Number(player, "total_fuel_litres"),
            ExperiencePoints = Integer(player, "experience_points"),
            TransportedCargoTypeCount = cargoTypes.Count,
            DeliveryLogCount = deliveryRefs.Count
        };
    }

    private static IReadOnlyList<string> ParseStringArray(SiiBlock? owner, string arrayName)
    {
        if (owner is null)
            return Array.Empty<string>();

        return GetArrayValues(owner, arrayName)
            .Select(CleanValue)
            .Where(x => !string.IsNullOrWhiteSpace(x) &&
                        !x.Equals("null", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<SaveDeliveryLogEntry> ParseDeliveryHistory(
        SiiBlock? player,
        IReadOnlyList<SiiBlock> blocks)
    {
        if (player is null)
            return Array.Empty<SaveDeliveryLogEntry>();

        var result = new List<SaveDeliveryLogEntry>();

        foreach (var reference in GetArrayValues(player, "delivery_log"))
        {
            var block = FindBlock(blocks, reference);
            if (block is null)
                continue;

            var parameters = GetArrayValues(block, "params")
                .Select(CleanValue)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            result.Add(new SaveDeliveryLogEntry
            {
                Id = block.Id,
                Parameters = parameters
            });
        }

        return result;
    }

    private static IReadOnlyList<string> GetArrayValues(SiiBlock owner, string arrayName) =>
        owner.Fields
            .Where(x => x.Key.StartsWith(arrayName + "[", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => ArrayIndex(x.Key))
            .Select(x => x.Value)
            .ToArray();

    private static int ArrayIndex(string key)
    {
        var open = key.LastIndexOf('[');
        var close = key.LastIndexOf(']');
        if (open < 0 || close <= open)
            return int.MaxValue;

        return int.TryParse(
            key[(open + 1)..close],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var index)
            ? index
            : int.MaxValue;
    }

    public TachographTicket CreateTachographTicket(GameSaveSnapshot snapshot)
    {
        var tachograph = snapshot.Tachograph ?? new SaveTachograph();

        return new TachographTicket
        {
            GeneratedAtUtc = DateTime.UtcNow,
            TruckPlate = snapshot.CurrentTruck?.LicensePlate ?? string.Empty,
            OdometerKm = snapshot.CurrentTruck?.OdometerKm ?? 0.0,
            DrivingMinutes = tachograph.DrivingMinutes,
            MinutesSinceMandatoryBreak = tachograph.MinutesSinceMandatoryBreak,
            BreakMinutes = tachograph.BreakMinutes,
            LastSleepGameMinutes = tachograph.LastSleepGameMinutes
        };
    }

    private static SaveTachograph ParseTachograph(SiiBlock? player)
    {
        if (player is null)
            return new SaveTachograph();

        return new SaveTachograph
        {
            DrivingMinutes = Integer(player, "driving_time"),
            MinutesSinceMandatoryBreak = Integer(player, "time_since_mandatory_break"),
            BreakMinutes = Integer(player, "on_break_time"),
            LastSleepGameMinutes = Integer(player, "last_sleep_time")
        };
    }

    private static SaveTruck? ParseCurrentTruck(
        SiiBlock? player,
        IReadOnlyList<SiiBlock> blocks)
    {
        if (player is null)
            return null;

        var truckRef = FirstReference(player, "assigned_truck", "current_truck", "truck");
        if (string.IsNullOrWhiteSpace(truckRef))
            return null;

        var truck = FindBlock(blocks, truckRef);
        return truck is null ? null : ParseTruckBlock(truck, blocks);
    }

    private static SaveTrailer? ParseCurrentTrailer(
        SiiBlock? player,
        IReadOnlyList<SiiBlock> blocks)
    {
        if (player is null)
            return null;

        var trailerRef = FirstReference(
            player,
            "assigned_trailer",
            "current_trailer",
            "trailer");

        if (string.IsNullOrWhiteSpace(trailerRef))
            return null;

        var trailer = FindBlock(blocks, trailerRef);
        return trailer is null ? null : ParseTrailer(trailer);
    }

    private static IReadOnlyList<SaveTruck> ParseFleetTrucks(
        SiiBlock? player,
        IReadOnlyList<SiiBlock> blocks)
    {
        if (player is null)
            return Array.Empty<SaveTruck>();

        var result = new List<SaveTruck>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in GetReferencedBlocks(player, blocks, "trucks")
                     .Concat(GetReferencedBlocks(player, blocks, "my_vehicles"))
                     .Concat(GetReferencedBlocks(player, blocks, "assigned_vehicles")))
        {
            if (!seen.Add(block.Id))
                continue;

            result.Add(ParseTruckBlock(block, blocks));
        }

        return result;
    }

    private static IReadOnlyList<SaveTrailer> ParseFleetTrailers(
        SiiBlock? player,
        IReadOnlyList<SiiBlock> blocks)
    {
        if (player is null)
            return Array.Empty<SaveTrailer>();

        var result = new List<SaveTrailer>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in GetReferencedBlocks(player, blocks, "trailers")
                     .Concat(GetReferencedBlocks(player, blocks, "my_trailers"))
                     .Concat(GetReferencedBlocks(player, blocks, "assigned_trailers")))
        {
            if (!seen.Add(block.Id))
                continue;

            result.Add(ParseTrailer(block));
        }

        return result;
    }

    private static SaveTruck ParseTruckBlock(
        SiiBlock truck,
        IReadOnlyList<SiiBlock> blocks)
    {
        var accessoryBlocks = GetReferencedBlocks(truck, blocks, "accessories");

        string Component(params string[] tokens)
        {
            foreach (var accessory in accessoryBlocks)
            {
                var definition = FirstValue(accessory, "data_path", "data", "definition", "def");
                if (string.IsNullOrWhiteSpace(definition))
                    continue;

                if (tokens.Any(t =>
                    definition.Contains("/" + t + "/", StringComparison.OrdinalIgnoreCase) ||
                    definition.Contains("/" + t + ".", StringComparison.OrdinalIgnoreCase)))
                    return definition;
            }

            return string.Empty;
        }

        return new SaveTruck
        {
            Id = truck.Id,
            Definition = FirstValue(truck, "data_path", "data", "definition", "def"),
            LicensePlate = FirstValue(truck, "license_plate"),
            LicensePlateCountry = ExtractPlateCountry(truck.Get("license_plate")),
            LicensePlateType = FirstValue(truck, "license_plate_type"),
            CabinDefinition = Component("cabin"),
            InteriorDefinition = Component("interior"),
            TransmissionDefinition = Component("transmission"),
            ChassisDefinition = Component("chassis"),
            EngineDefinition = Component("engine"),
            OdometerKm = Number(truck, "odometer"),
            IntegrityOdometerKm = Number(truck, "integrity_odometer"),
            FuelRelative = Number(truck, "fuel_relative"),
            TripFuelLiters = Number(truck, "trip_fuel_l"),
            TripDistanceKm = Number(truck, "trip_distance_km"),
            TripTimeMinutes = Number(truck, "trip_time_min"),
            EngineWear = Number(truck, "engine_wear"),
            TransmissionWear = Number(truck, "transmission_wear"),
            CabinWear = Number(truck, "cabin_wear"),
            ChassisWear = Number(truck, "chassis_wear"),
            WheelsWear = Number(truck, "wheels_wear"),
            EngineWearUnfixable = Number(truck, "engine_wear_unfixable"),
            TransmissionWearUnfixable = Number(truck, "transmission_wear_unfixable"),
            CabinWearUnfixable = Number(truck, "cabin_wear_unfixable"),
            ChassisWearUnfixable = Number(truck, "chassis_wear_unfixable"),
            WheelsWearUnfixable = Number(truck, "wheels_wear_unfixable")
        };
    }

    private static SaveTrailer ParseTrailer(SiiBlock trailer) =>
        new()
        {
            Id = trailer.Id,
            Definition = FirstValue(trailer, "data_path", "data", "definition", "def"),
            LicensePlate = FirstValue(trailer, "license_plate"),
            LicensePlateCountry = ExtractPlateCountry(trailer.Get("license_plate")),
            LicensePlateType = FirstValue(trailer, "license_plate_type"),
            CargoMassKg = Number(trailer, "cargo_mass"),
            CargoDamage = Number(trailer, "cargo_damage"),
            TrailerBodyWear = Number(trailer, "trailer_body_wear"),
            ChassisWear = Number(trailer, "chassis_wear"),
            WheelsWear = Number(trailer, "wheels_wear"),
            TrailerBodyWearUnfixable = Number(trailer, "trailer_body_wear_unfixable"),
            ChassisWearUnfixable = Number(trailer, "chassis_wear_unfixable"),
            WheelsWearUnfixable = Number(trailer, "wheels_wear_unfixable")
        };

    private static IReadOnlyList<SiiBlock> GetReferencedBlocks(
        SiiBlock owner,
        IReadOnlyList<SiiBlock> blocks,
        string arrayName)
    {
        var result = new List<SiiBlock>();

        foreach (var field in owner.Fields
                     .Where(x => x.Key.StartsWith(arrayName + "[", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(x => ArrayIndex(x.Key)))
        {
            var reference = CleanValue(field.Value);
            var block = FindBlock(blocks, reference);
            if (block is not null)
                result.Add(block);
        }

        return result;
    }

    private static SiiBlock? FindBlock(IReadOnlyList<SiiBlock> blocks, string id)
    {
        var cleanId = CleanValue(id);
        return blocks.FirstOrDefault(b =>
            b.Id.Equals(cleanId, StringComparison.OrdinalIgnoreCase));
    }

    private static string FirstReference(SiiBlock block, params string[] keys) =>
        FirstValue(block, keys);

    private static string FirstValue(SiiBlock block, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = block.GetCleanString(key);
            if (!string.IsNullOrWhiteSpace(value) &&
                !value.Equals("null", StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return string.Empty;
    }

    private static double Number(SiiBlock block, string key) =>
        TryParseDouble(block.Get(key), out var value) ? value : 0.0;

    private static int Integer(SiiBlock block, string key)
    {
        var value = CleanValue(block.Get(key));
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            return integer;

        return TryParseDouble(value, out var number)
            ? (int)Math.Round(number, MidpointRounding.AwayFromZero)
            : 0;
    }

    private static string ExtractPlateCountry(string? rawPlate)
    {
        var value = CleanValue(rawPlate);
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var separator = value.LastIndexOf('|');
        if (separator < 0 || separator == value.Length - 1)
            return string.Empty;

        return value[(separator + 1)..].Trim();
    }

    internal static string CleanValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var result = value.Trim();

        var commentIndex = result.IndexOf('#');
        if (commentIndex >= 0)
            result = result[..commentIndex].Trim();

        if (result.Length >= 2 &&
            ((result[0] == '"' && result[^1] == '"') ||
             (result[0] == '\'' && result[^1] == '\'')))
            result = result[1..^1];

        return result;
    }

    internal static bool TryParseDouble(string? value, out double result)
    {
        return double.TryParse(
            CleanValue(value).Replace("&", string.Empty),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);
    }
}

public sealed record SiiBlock(
    string Type,
    string Id,
    IReadOnlyDictionary<string, string> Fields)
{
    public string? Get(string key) =>
        Fields.TryGetValue(key, out var value) ? value : null;

    public string? GetCleanString(string key)
    {
        var value = Get(key);
        return string.IsNullOrWhiteSpace(value) ? null : GameSiiParser.CleanValue(value);
    }
}
