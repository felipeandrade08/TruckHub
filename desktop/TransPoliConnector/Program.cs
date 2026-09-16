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

        private static void Main()
        {
            Console.Title = "TransPoli Connector";
            Console.WriteLine("TransPoli Connector - ETS2");
            Console.WriteLine("Criado por Felipe Andrade");
            Console.WriteLine("Bridge local: " + Prefix);
            Console.WriteLine("Fonte: Memory Mapped File " + MemoryMapName);
            Console.WriteLine();
            using (var listener = new HttpListener())
            {
                listener.Prefixes.Add(Prefix);
                listener.Start();
                Console.CancelKeyPress += (sender, e) => { e.Cancel = true; _running = false; try { listener.Stop(); } catch { } };
                var telemetryThread = new Thread(TelemetryLoop) { IsBackground = true, Name = "TransPoliTelemetry" };
                telemetryThread.Start();
                Console.WriteLine("Connector iniciado.");
                Console.WriteLine("Aguardando o ETS2 criar a telemetria...");
                Console.WriteLine("Pressione Ctrl+C para encerrar.");
                while (_running && listener.IsListening)
                {
                    try { var context = listener.GetContext(); ThreadPool.QueueUserWorkItem(_ => Handle(context)); }
                    catch (HttpListenerException) { break; }
                    catch (ObjectDisposedException) { break; }
                }
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
                    using (var view = map.CreateViewStream(0, MemoryMapSize, MemoryMappedFileAccess.Read))
                    using (var reader = new BinaryReader(view, Encoding.UTF8, true))
                    { var next = ReadSnapshot(reader); lock (Sync) { _snapshot = next; } }
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
            const int Zone2 = 40;
            const int Zone3 = 500;
            const int Zone4 = 700;
            const int Zone5 = 1500;
            const int Zone9 = 2300;
            const int JobIncomeOffset = 4000;

            var sdkActive = ReadBool(reader, 0);
            var paused = ReadBool(reader, 4);
            var timestamp = ReadUInt64(reader, 8);
            var game = ReadUInt32(reader, Zone2 + 12);
            var plannedDistance = ReadUInt32(reader, Zone2 + 60);
            var jobIncome = ReadUInt64(reader, JobIncomeOffset);

            // Layout usado pelo conector atual: truck_f inicia em Zone4 + 4 + 244.
            // Os novos campos seguem a ordem oficial do SCS telemetry truck_f.
            const int TruckFloat = Zone4 + 4 + 244;
            var speed = ReadFloat(reader, TruckFloat + (0 * 4));
            var rpm = ReadFloat(reader, TruckFloat + (1 * 4));
            var userBrake = ReadFloat(reader, TruckFloat + (4 * 4));
            var gameBrake = ReadFloat(reader, TruckFloat + (8 * 4));
            var cruiseSpeed = ReadFloat(reader, TruckFloat + (10 * 4));
            var airPressure = ReadFloat(reader, TruckFloat + (11 * 4));
            var brakeTemperature = ReadFloat(reader, TruckFloat + (12 * 4));
            var fuel = ReadFloat(reader, TruckFloat + (13 * 4));
            var fuelRange = ReadFloat(reader, TruckFloat + (15 * 4));
            var odometer = ReadFloat(reader, TruckFloat + (27 * 4));
            var routeDistance = ReadFloat(reader, TruckFloat + (28 * 4));
            var wearEngine = ReadFloat(reader, TruckFloat + (34 * 4));
            var wearTransmission = ReadFloat(reader, TruckFloat + (35 * 4));
            var wearCabin = ReadFloat(reader, TruckFloat + (36 * 4));
            var wearChassis = ReadFloat(reader, TruckFloat + (37 * 4));
            var wearWheels = ReadFloat(reader, TruckFloat + (38 * 4));
            var cargoDamage = ReadFloat(reader, TruckFloat + (63 * 4));

            var gear = ReadGear(reader);
            const int TruckBool = Zone5 + 18;
            var cargoLoaded = ReadBool(reader, Zone5 + 16);
            var engineEnabled = ReadBool(reader, TruckBool + 10);
            var motorBrake = ReadBool(reader, TruckBool + 1);
            var parkingBrake = ReadBool(reader, TruckBool + 0);
            var brakeLight = ReadBool(reader, TruckBool + 17);
            var cruiseControl = ReadBool(reader, TruckBool + 23);

            var truckBrand = ReadString(reader, Zone9 + 64);
            var truckId = ReadString(reader, Zone9 + 128);
            var truckName = ReadString(reader, Zone9 + 192);
            var cargo = ReadString(reader, Zone9 + 320);
            var destinationCity = ReadString(reader, Zone9 + 448);
            var destinationCompany = ReadString(reader, Zone9 + 576);
            var sourceCity = ReadString(reader, Zone9 + 704);
            var sourceCompany = ReadString(reader, Zone9 + 832);
            var licensePlate = ReadString(reader, Zone9 + 912);
            var cargoMass = ReadFloat(reader, Zone4 + 4 + (11 * 4));
            var gameName = game == 1 ? "ETS2" : game == 2 ? "ATS" : "Unknown";

            return new TelemetrySnapshot
            {
                Connected = sdkActive, Updated = sdkActive, Timestamp = timestamp, Game = gameName, GamePaused = paused,
                TruckBrand = Clean(truckBrand), TruckModel = Clean(truckName), TruckId = Clean(truckId), LicensePlate = Clean(licensePlate),
                EngineEnabled = engineEnabled, CargoLoaded = cargoLoaded, SpeedKph = speed * 3.6f, SpeedMps = speed, Rpm = rpm, Gear = gear,
                FuelLiters = fuel, FuelRangeKm = fuelRange, OdometerKm = odometer, CruiseControl = cruiseControl || cruiseSpeed > 0.1f,
                SourceCity = Clean(sourceCity), DestinationCity = Clean(destinationCity), SourceCompany = Clean(sourceCompany), DestinationCompany = Clean(destinationCompany),
                Cargo = Clean(cargo), CargoMassKg = cargoMass, PlannedDistanceKm = plannedDistance > 0 ? plannedDistance : (uint)Math.Max(0, routeDistance),
                CargoValueBrl = jobIncome > 0 ? jobIncome : (ulong?)null,
                UserBrake = Clamp01(userBrake), EffectiveBrake = Clamp01(gameBrake), BrakeTemperature = brakeTemperature,
                AirPressure = airPressure, MotorBrake = motorBrake, ParkingBrake = parkingBrake, BrakeLight = brakeLight,
                WearEngine = Clamp01(wearEngine), WearTransmission = Clamp01(wearTransmission), WearCabin = Clamp01(wearCabin),
                WearChassis = Clamp01(wearChassis), WearWheels = Clamp01(wearWheels), CargoDamage = Clamp01(cargoDamage)
            };
        }

        private static int ReadGear(BinaryReader reader)
        {
            var gear = ReadInt32(reader, Zone3Offset());
            var dashboardGear = ReadInt32(reader, Zone3Offset() + 4);
            if (gear >= -20 && gear <= 30) return gear;
            if (dashboardGear >= -20 && dashboardGear <= 30) return dashboardGear;
            return 0;
        }
        private static int Zone3Offset() { return 500; }
        private static float Clamp01(float value) { return float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Max(0, Math.Min(1, value)); }
        private static bool ReadBool(BinaryReader reader, long offset) { reader.BaseStream.Position = offset; return reader.ReadByte() != 0; }
        private static int ReadInt32(BinaryReader reader, long offset) { reader.BaseStream.Position = offset; return reader.ReadInt32(); }
        private static uint ReadUInt32(BinaryReader reader, long offset) { reader.BaseStream.Position = offset; return reader.ReadUInt32(); }
        private static ulong ReadUInt64(BinaryReader reader, long offset) { reader.BaseStream.Position = offset; return reader.ReadUInt64(); }
        private static float ReadFloat(BinaryReader reader, long offset) { reader.BaseStream.Position = offset; return reader.ReadSingle(); }
        private static string ReadString(BinaryReader reader, long offset) { reader.BaseStream.Position = offset; var bytes = reader.ReadBytes(64); var length = Array.IndexOf(bytes, (byte)0); if (length < 0) length = bytes.Length; return Encoding.UTF8.GetString(bytes, 0, length); }
        private static string Clean(string value) { return string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
        private static void SetDisconnected() { lock (Sync) { _snapshot = TelemetrySnapshot.Disconnected(); } }
        private static void CloseMap(ref MemoryMappedFile map) { if (map == null) return; try { map.Dispose(); } catch { } map = null; }

        private static void Handle(HttpListenerContext context)
        {
            try
            {
                var path = context.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
                TelemetrySnapshot current; lock (Sync) { current = _snapshot; }
                if (path == "" || path == "/health") { WriteJson(context, new { ok = true, service = "TransPoli Connector", version = "0.3.0", telemetryConnected = current.Connected, source = MemoryMapName }); return; }
                if (path == "/telemetry") { WriteJson(context, current); return; }
                context.Response.StatusCode = 404; WriteJson(context, new { ok = false, error = "Endpoint não encontrado." });
            }
            catch (Exception ex) { try { context.Response.StatusCode = 500; WriteJson(context, new { ok = false, error = ex.Message }); } catch { } }
            finally { try { context.Response.Close(); } catch { } }
        }
        private static void WriteJson(HttpListenerContext context, object value) { var json = Json.Serialize(value); var bytes = Encoding.UTF8.GetBytes(json); context.Response.ContentType = "application/json; charset=utf-8"; context.Response.ContentLength64 = bytes.Length; context.Response.Headers["Cache-Control"] = "no-store"; context.Response.OutputStream.Write(bytes, 0, bytes.Length); }
    }

    public sealed class TelemetrySnapshot
    {
        public bool Connected { get; set; } public bool Updated { get; set; } public ulong Timestamp { get; set; } public string Game { get; set; }
        public bool GamePaused { get; set; } public string TruckBrand { get; set; } public string TruckModel { get; set; } public string TruckId { get; set; }
        public string LicensePlate { get; set; } public bool EngineEnabled { get; set; } public bool CargoLoaded { get; set; } public float SpeedKph { get; set; }
        public float SpeedMps { get; set; } public float Rpm { get; set; } public int Gear { get; set; } public float FuelLiters { get; set; } public float FuelRangeKm { get; set; }
        public float OdometerKm { get; set; } public bool CruiseControl { get; set; } public string SourceCity { get; set; } public string DestinationCity { get; set; }
        public string SourceCompany { get; set; } public string DestinationCompany { get; set; } public string Cargo { get; set; } public float CargoMassKg { get; set; }
        public uint PlannedDistanceKm { get; set; } public ulong? CargoValueBrl { get; set; }
        public float UserBrake { get; set; } public float EffectiveBrake { get; set; } public float BrakeTemperature { get; set; } public float AirPressure { get; set; }
        public bool MotorBrake { get; set; } public bool ParkingBrake { get; set; } public bool BrakeLight { get; set; }
        public float WearEngine { get; set; } public float WearTransmission { get; set; } public float WearCabin { get; set; } public float WearChassis { get; set; }
        public float WearWheels { get; set; } public float CargoDamage { get; set; }
        public static TelemetrySnapshot Disconnected() { return new TelemetrySnapshot { Connected = false, Updated = false, Game = "Unknown", CargoLoaded = false }; }
    }
}
