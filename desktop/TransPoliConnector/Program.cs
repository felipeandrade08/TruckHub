using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace TransPoliConnector
{
    internal static class Program
    {
        private const string Prefix = "http://127.0.0.1:17877/";
        private const string MemoryMapName = "Local\\SCSTelemetry";
        private const int MemoryMapSize = 32 * 1024;
        private static readonly object Sync = new object();
        private static TelemetrySnapshot _snapshot = TelemetrySnapshot.Disconnected();
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static volatile bool _running = true;
        private static bool _lastTollgateFlag;
        private static long _lastTollgateAmount;
        private static long _tollgateSequence;
        private static DateTime _tollgatePulseUntilUtc = DateTime.MinValue;
        private static void Main()
        {
            Console.Title = "TransPoli Connector"; Console.WriteLine("TransPoli Connector - ETS2"); Console.WriteLine("Criado por Felipe Andrade"); Console.WriteLine("Bridge local: " + Prefix); Console.WriteLine("Fonte: Memory Mapped File " + MemoryMapName); Console.WriteLine();
            using (var listener = new HttpListener())
            {
                listener.Prefixes.Add(Prefix); listener.Start(); Console.CancelKeyPress += (sender, e) => { e.Cancel = true; _running = false; try { listener.Stop(); } catch { } };
                var telemetryThread = new Thread(TelemetryLoop) { IsBackground = true, Name = "TransPoliTelemetry" }; telemetryThread.Start(); Console.WriteLine("Connector iniciado."); Console.WriteLine("Aguardando o ETS2 criar a telemetria..."); Console.WriteLine("Pressione Ctrl+C para encerrar.");
                while (_running && listener.IsListening) { try { var context = listener.GetContext(); ThreadPool.QueueUserWorkItem(_ => Handle(context)); } catch (HttpListenerException) { break; } catch (ObjectDisposedException) { break; } }
            }
        }
        private static void TelemetryLoop()
        {
            MemoryMappedFile map = null;
            while (_running)
            {
                try
                {
                    if (map == null) { map = MemoryMappedFile.OpenExisting(MemoryMapName, MemoryMappedFileRights.Read); Console.WriteLine("[ETS2] Memória de telemetria encontrada."); }
                    using (var view = map.CreateViewStream(0, MemoryMapSize, MemoryMappedFileAccess.Read)) using (var reader = new BinaryReader(view, Encoding.UTF8, true)) { var next = ReadSnapshot(reader); lock (Sync) { _snapshot = next; } }
                    Thread.Sleep(100);
                }
                catch (FileNotFoundException) { SetDisconnected(); CloseMap(ref map); Thread.Sleep(1000); }
                catch (UnauthorizedAccessException) { SetDisconnected(); CloseMap(ref map); Thread.Sleep(1000); }
                catch (IOException) { SetDisconnected(); CloseMap(ref map); Thread.Sleep(1000); }
                catch (Exception ex) { SetDisconnected(); CloseMap(ref map); Console.WriteLine("[ETS2] Erro de telemetria: " + ex.Message); Thread.Sleep(1000); }
            }
            CloseMap(ref map);
        }
        private static TelemetrySnapshot ReadSnapshot(BinaryReader reader)
        {
            const int Zone2 = 40, Zone3 = 500, Zone4 = 700, Zone5 = 1500, Zone8 = 2200, Zone9 = 2300, JobIncomeOffset = 4000, SpecialEventsOffset = 4300;
            var sdkActive = ReadBool(reader, 0); var paused = ReadBool(reader, 4); var timestamp = ReadUInt64(reader, 8); var game = ReadUInt32(reader, Zone2 + 12); var plannedDistance = ReadUInt32(reader, Zone2 + 60); var retarderLevel = ReadUInt32(reader, 108); var jobIncome = ReadUInt64(reader, JobIncomeOffset);
            const int TruckFloat = Zone4 + 4 + 244;
            var speed = ReadFloat(reader, TruckFloat + 0); var rpm = ReadFloat(reader, TruckFloat + 4); var userThrottle = ReadFloat(reader, TruckFloat + 12); var userBrake = ReadFloat(reader, TruckFloat + 16); var gameThrottle = ReadFloat(reader, TruckFloat + 28); var gameBrake = ReadFloat(reader, TruckFloat + 32); var cruiseSpeed = ReadFloat(reader, TruckFloat + 40); var airPressure = ReadFloat(reader, TruckFloat + 44); var brakeTemperature = ReadFloat(reader, TruckFloat + 48); var fuel = ReadFloat(reader, TruckFloat + 52); var fuelAvgConsumption = ReadFloat(reader, TruckFloat + 56); var fuelRange = ReadFloat(reader, TruckFloat + 60); var adblue = ReadFloat(reader, TruckFloat + 64); var oilPressure = ReadFloat(reader, TruckFloat + 68); var oilTemperature = ReadFloat(reader, TruckFloat + 72); var waterTemperature = ReadFloat(reader, TruckFloat + 76); var batteryVoltage = ReadFloat(reader, TruckFloat + 80);
            var wearEngine = ReadFloat(reader, TruckFloat + 88); var wearTransmission = ReadFloat(reader, TruckFloat + 92); var wearCabin = ReadFloat(reader, TruckFloat + 96); var wearChassis = ReadFloat(reader, TruckFloat + 100); var wearWheels = ReadFloat(reader, TruckFloat + 104); var odometer = ReadFloat(reader, TruckFloat + 108); var routeDistance = ReadFloat(reader, TruckFloat + 112); var routeTime = ReadFloat(reader, TruckFloat + 116); var speedLimit = ReadFloat(reader, TruckFloat + 120);
            // truck_f has six 16-float wheel arrays after speedLimit. gameplay_f starts at index 127 and job_f.cargoDamage at index 130.
            var cargoDamage = ReadFloat(reader, TruckFloat + (130 * 4)); var refuelAmount = ReadFloat(reader, TruckFloat + (129 * 4));
            // SCS telemetry shared-memory zone 8 (dplacement): world X/Y/Z + heading/pitch/roll.\n            // Orientation is normalized by the SDK to turns (0..1), so expose degrees too.\n            var worldX = ReadDouble(reader, Zone8 + 0); var worldY = ReadDouble(reader, Zone8 + 8); var worldZ = ReadDouble(reader, Zone8 + 16);\n            var heading = ReadDouble(reader, Zone8 + 24); var pitch = ReadDouble(reader, Zone8 + 32); var roll = ReadDouble(reader, Zone8 + 40);\n            var positionValid = IsFinite(worldX) && IsFinite(worldY) && IsFinite(worldZ) && (Math.Abs(worldX) > 0.001 || Math.Abs(worldY) > 0.001 || Math.Abs(worldZ) > 0.001);\n            var headingDeg = NormalizeDegrees(heading * 360.0); var pitchDeg = NormalizeDegrees(pitch * 360.0); var rollDeg = NormalizeDegrees(roll * 360.0);\n            var gear = ReadGear(reader);
            const int ConfigBool = Zone5, TruckBool = Zone5 + 66;
            var cargoLoaded = ReadBool(reader, ConfigBool + 64); var specialJob = ReadBool(reader, ConfigBool + 65); var parkingBrake = ReadBool(reader, TruckBool + 0); var motorBrake = ReadBool(reader, TruckBool + 1); var airPressureWarning = ReadBool(reader, TruckBool + 2); var airPressureEmergency = ReadBool(reader, TruckBool + 3); var fuelWarning = ReadBool(reader, TruckBool + 4); var adblueWarning = ReadBool(reader, TruckBool + 5); var oilPressureWarning = ReadBool(reader, TruckBool + 6); var waterTemperatureWarning = ReadBool(reader, TruckBool + 7); var batteryVoltageWarning = ReadBool(reader, TruckBool + 8); var electricEnabled = ReadBool(reader, TruckBool + 9); var engineEnabled = ReadBool(reader, TruckBool + 10); var wipers = ReadBool(reader, TruckBool + 11); var blinkerLeftActive = ReadBool(reader, TruckBool + 12); var blinkerRightActive = ReadBool(reader, TruckBool + 13); var blinkerLeftOn = ReadBool(reader, TruckBool + 14); var blinkerRightOn = ReadBool(reader, TruckBool + 15); var lightsParking = ReadBool(reader, TruckBool + 16); var lightsBrake = ReadBool(reader, TruckBool + 20); var lightsReverse = ReadBool(reader, TruckBool + 21); var lightsHazard = ReadBool(reader, TruckBool + 22); var cruiseControl = ReadBool(reader, TruckBool + 23); var differentialLock = ReadBool(reader, TruckBool + 32); var liftAxle = ReadBool(reader, TruckBool + 33); var liftAxleIndicator = ReadBool(reader, TruckBool + 34); var trailerLiftAxle = ReadBool(reader, TruckBool + 35); var trailerLiftAxleIndicator = ReadBool(reader, TruckBool + 36);
            var onJob = ReadBool(reader, SpecialEventsOffset + 0); var jobFinished = ReadBool(reader, SpecialEventsOffset + 1); var jobCancelled = ReadBool(reader, SpecialEventsOffset + 2); var jobDelivered = ReadBool(reader, SpecialEventsOffset + 3); var fined = ReadBool(reader, SpecialEventsOffset + 4); var tollgate = ReadBool(reader, SpecialEventsOffset + 5); var refuel = ReadBool(reader, SpecialEventsOffset + 8); var refuelPayed = ReadBool(reader, SpecialEventsOffset + 9);
            var tollgateAmount = ReadInt64(reader, 4200 + 24);
            var tollgatePaid = tollgate != _lastTollgateFlag && tollgateAmount > 0;
            if (tollgatePaid) { _lastTollgateAmount = tollgateAmount; _tollgateSequence++; _tollgatePulseUntilUtc = DateTime.UtcNow.AddSeconds(3); }
            _lastTollgateFlag = tollgate;
            var truckBrand = ReadString(reader, Zone9 + 64); var truckId = ReadString(reader, Zone9 + 128); var truckName = ReadString(reader, Zone9 + 192); var cargo = ReadString(reader, Zone9 + 320); var destinationCity = ReadString(reader, Zone9 + 448); var destinationCompany = ReadString(reader, Zone9 + 576); var sourceCity = ReadString(reader, Zone9 + 704); var sourceCompany = ReadString(reader, Zone9 + 832); var licensePlate = ReadString(reader, Zone9 + 912); var cargoMass = ReadFloat(reader, Zone4 + 4 + (11 * 4)); var gameName = game == 1 ? "ETS2" : game == 2 ? "ATS" : "Unknown";
            return new TelemetrySnapshot
            {
                Connected = sdkActive, Updated = sdkActive, Timestamp = timestamp, Game = gameName, TollgatePaid = DateTime.UtcNow < _tollgatePulseUntilUtc, TollgateAmount = DateTime.UtcNow < _tollgatePulseUntilUtc ? _lastTollgateAmount : 0, TollgateEventId = DateTime.UtcNow < _tollgatePulseUntilUtc ? _tollgateSequence : 0, GamePaused = paused, TruckBrand = Clean(truckBrand), TruckModel = Clean(truckName), TruckId = Clean(truckId), LicensePlate = Clean(licensePlate), EngineEnabled = engineEnabled, ElectricEnabled = electricEnabled, CargoLoaded = cargoLoaded, SpecialJob = specialJob, OnJob = onJob, JobFinished = jobFinished, JobCancelled = jobCancelled, JobDelivered = jobDelivered, RefuelActive = refuel, RefuelPayed = refuelPayed, RefuelAmountLiters = SafeNonNegative(refuelAmount),
                WorldX = SafeDouble(worldX), WorldY = SafeDouble(worldY), WorldZ = SafeDouble(worldZ), HeadingDeg = SafeDouble(headingDeg), PitchDeg = SafeDouble(pitchDeg), RollDeg = SafeDouble(rollDeg), PositionValid = positionValid,\n                SpeedKph = SafeFloat(speed * 3.6f), SpeedMps = SafeFloat(speed), SpeedLimitKph = SafeFloat(speedLimit * 3.6f), Rpm = SafeFloat(rpm), Gear = gear, UserThrottle = Clamp01(userThrottle), EffectiveThrottle = Clamp01(gameThrottle), UserBrake = Clamp01(userBrake), EffectiveBrake = Clamp01(gameBrake), FuelLiters = SafeNonNegative(fuel), FuelAvgConsumption = SafeNonNegative(fuelAvgConsumption), FuelRangeKm = SafeNonNegative(fuelRange), AdBlueLiters = SafeNonNegative(adblue), OilPressure = SafeNonNegative(oilPressure), OilTemperature = SafeFloat(oilTemperature), WaterTemperature = SafeFloat(waterTemperature), BatteryVoltage = SafeNonNegative(batteryVoltage), OdometerKm = SafeNonNegative(odometer), RouteDistanceKm = SafeNonNegative(routeDistance) / 1000f, RouteTimeSeconds = SafeNonNegative(routeTime), CruiseControl = cruiseControl || cruiseSpeed > 0.1f, CruiseSpeedKph = SafeNonNegative(cruiseSpeed * 3.6f), SourceCity = Clean(sourceCity), DestinationCity = Clean(destinationCity), SourceCompany = Clean(sourceCompany), DestinationCompany = Clean(destinationCompany), Cargo = Clean(cargo), CargoMassKg = SafeNonNegative(cargoMass), PlannedDistanceKm = plannedDistance > 0 ? plannedDistance : (uint)Math.Max(0, routeDistance / 1000f), CargoValueBrl = jobIncome > 0 ? jobIncome : (ulong?)null,
                AirPressure = SafeNonNegative(airPressure), BrakeTemperature = SafeFloat(brakeTemperature), MotorBrake = motorBrake, ParkingBrake = parkingBrake, BrakeLight = lightsBrake, AirPressureWarning = airPressureWarning, AirPressureEmergency = airPressureEmergency, FuelWarning = fuelWarning, AdBlueWarning = adblueWarning, OilPressureWarning = oilPressureWarning, WaterTemperatureWarning = waterTemperatureWarning, BatteryVoltageWarning = batteryVoltageWarning, Wipers = wipers, BlinkerLeftActive = blinkerLeftActive, BlinkerRightActive = blinkerRightActive, BlinkerLeftOn = blinkerLeftOn, BlinkerRightOn = blinkerRightOn, LightsParking = lightsParking, LightsBrake = lightsBrake, LightsReverse = lightsReverse, LightsHazard = lightsHazard, DifferentialLock = differentialLock, LiftAxle = liftAxle, LiftAxleIndicator = liftAxleIndicator, TrailerLiftAxle = trailerLiftAxle, TrailerLiftAxleIndicator = trailerLiftAxleIndicator, RetarderLevel = retarderLevel, WearEngine = Clamp01(wearEngine), WearTransmission = Clamp01(wearTransmission), WearCabin = Clamp01(wearCabin), WearChassis = Clamp01(wearChassis), WearWheels = Clamp01(wearWheels), CargoDamage = Clamp01(cargoDamage)
            };
        }
        private static int ReadGear(BinaryReader reader) { var gear = ReadInt32(reader, Zone3Offset()); var dashboardGear = ReadInt32(reader, Zone3Offset() + 4); if (gear >= -20 && gear <= 30) return gear; if (dashboardGear >= -20 && dashboardGear <= 30) return dashboardGear; return 0; }
        private static int Zone3Offset() { return 500; }
        private static float SafeFloat(float value) { return float.IsNaN(value) || float.IsInfinity(value) ? 0 : value; }
        private static float SafeNonNegative(float value) { return Math.Max(0, SafeFloat(value)); }
        private static float Clamp01(float value) { return Math.Max(0, Math.Min(1, SafeFloat(value))); }
        private static bool ReadBool(BinaryReader reader, long offset) { reader.BaseStream.Position = offset; return reader.ReadByte() != 0; }
        private static int ReadInt32(BinaryReader reader, long offset) { reader.BaseStream.Position = offset; return reader.ReadInt32(); }
        private static uint ReadUInt32(BinaryReader reader, long offset) { reader.BaseStream.Position = offset; return reader.ReadUInt32(); }
        private static ulong ReadUInt64(BinaryReader reader, long offset) { reader.BaseStream.Position = offset; return reader.ReadUInt64(); }
        private static long ReadInt64(BinaryReader reader, long offset) { reader.BaseStream.Position = offset; return reader.ReadInt64(); }
        private static float ReadFloat(BinaryReader reader, long offset) { reader.BaseStream.Position = offset; return reader.ReadSingle(); }\n        private static double ReadDouble(BinaryReader reader, long offset) { reader.BaseStream.Position = offset; return reader.ReadDouble(); }\n        private static bool IsFinite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }\n        private static double SafeDouble(double value) { return IsFinite(value) ? value : 0d; }\n        private static double NormalizeDegrees(double value) { if (!IsFinite(value)) return 0d; value %= 360d; if (value < 0d) value += 360d; return value; }
        private static string ReadString(BinaryReader reader, long offset) { reader.BaseStream.Position = offset; var bytes = reader.ReadBytes(64); var length = Array.IndexOf(bytes, (byte)0); if (length < 0) length = bytes.Length; return Encoding.UTF8.GetString(bytes, 0, length); }
        private static string Clean(string value) { return string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
        private static void SetDisconnected() { lock (Sync) { _snapshot = TelemetrySnapshot.Disconnected(); } }
        private static void CloseMap(ref MemoryMappedFile map) { if (map == null) return; try { map.Dispose(); } catch { } map = null; }
        private static void Handle(HttpListenerContext context)
        {
            try { var path = context.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant(); TelemetrySnapshot current; lock (Sync) { current = _snapshot; } if (path == "" || path == "/health") { WriteJson(context, new { ok = true, service = "TransPoli Connector", version = "0.4.1", telemetryConnected = current.Connected, source = MemoryMapName }); return; } if (path == "/telemetry") { WriteJson(context, current); return; } context.Response.StatusCode = 404; WriteJson(context, new { ok = false, error = "Endpoint não encontrado." }); }
            catch (Exception ex) { try { context.Response.StatusCode = 500; WriteJson(context, new { ok = false, error = ex.Message }); } catch { } }
            finally { try { context.Response.Close(); } catch { } }
        }
        private static void WriteJson(HttpListenerContext context, object value) { var json = Json.Serialize(value); var bytes = Encoding.UTF8.GetBytes(json); context.Response.ContentType = "application/json; charset=utf-8"; context.Response.ContentLength64 = bytes.Length; context.Response.Headers["Cache-Control"] = "no-store"; context.Response.OutputStream.Write(bytes, 0, bytes.Length); }
    }
    public sealed class TelemetrySnapshot
    {
        public bool Connected { get; set; } public bool Updated { get; set; } public ulong Timestamp { get; set; } public string Game { get; set; } public bool TollgatePaid { get; set; } public long TollgateAmount { get; set; } public long TollgateEventId { get; set; } public bool GamePaused { get; set; } public string TruckBrand { get; set; } public string TruckModel { get; set; } public string TruckId { get; set; } public string LicensePlate { get; set; } public bool EngineEnabled { get; set; } public bool ElectricEnabled { get; set; } public bool CargoLoaded { get; set; } public bool SpecialJob { get; set; } public bool OnJob { get; set; } public bool JobFinished { get; set; } public bool JobCancelled { get; set; } public bool JobDelivered { get; set; } public bool RefuelActive { get; set; } public bool RefuelPayed { get; set; } public float RefuelAmountLiters { get; set; }
        public double WorldX { get; set; } public double WorldY { get; set; } public double WorldZ { get; set; } public double HeadingDeg { get; set; } public double PitchDeg { get; set; } public double RollDeg { get; set; } public bool PositionValid { get; set; }\n        public float SpeedKph { get; set; } public float SpeedMps { get; set; } public float SpeedLimitKph { get; set; } public float Rpm { get; set; } public int Gear { get; set; } public float UserThrottle { get; set; } public float EffectiveThrottle { get; set; } public float UserBrake { get; set; } public float EffectiveBrake { get; set; } public float FuelLiters { get; set; } public float FuelAvgConsumption { get; set; } public float FuelRangeKm { get; set; } public float AdBlueLiters { get; set; } public float OilPressure { get; set; } public float OilTemperature { get; set; } public float WaterTemperature { get; set; } public float BatteryVoltage { get; set; } public float OdometerKm { get; set; } public float RouteDistanceKm { get; set; } public float RouteTimeSeconds { get; set; } public bool CruiseControl { get; set; } public float CruiseSpeedKph { get; set; }
        public string SourceCity { get; set; } public string DestinationCity { get; set; } public string SourceCompany { get; set; } public string DestinationCompany { get; set; } public string Cargo { get; set; } public float CargoMassKg { get; set; } public uint PlannedDistanceKm { get; set; } public ulong? CargoValueBrl { get; set; } public float AirPressure { get; set; } public float BrakeTemperature { get; set; } public bool MotorBrake { get; set; } public bool ParkingBrake { get; set; } public bool BrakeLight { get; set; } public bool AirPressureWarning { get; set; } public bool AirPressureEmergency { get; set; } public bool FuelWarning { get; set; } public bool AdBlueWarning { get; set; } public bool OilPressureWarning { get; set; } public bool WaterTemperatureWarning { get; set; } public bool BatteryVoltageWarning { get; set; }
        public bool Wipers { get; set; } public bool BlinkerLeftActive { get; set; } public bool BlinkerRightActive { get; set; } public bool BlinkerLeftOn { get; set; } public bool BlinkerRightOn { get; set; } public bool LightsParking { get; set; } public bool LightsBrake { get; set; } public bool LightsReverse { get; set; } public bool LightsHazard { get; set; } public bool DifferentialLock { get; set; } public bool LiftAxle { get; set; } public bool LiftAxleIndicator { get; set; } public bool TrailerLiftAxle { get; set; } public bool TrailerLiftAxleIndicator { get; set; } public uint RetarderLevel { get; set; } public float WearEngine { get; set; } public float WearTransmission { get; set; } public float WearCabin { get; set; } public float WearChassis { get; set; } public float WearWheels { get; set; } public float CargoDamage { get; set; }
        public static TelemetrySnapshot Disconnected() { return new TelemetrySnapshot { Connected = false, Updated = false, Game = "Unknown", CargoLoaded = false }; }
    }
}
