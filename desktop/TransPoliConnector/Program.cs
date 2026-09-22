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
            var sdkActive = ReadBool(reader, 0); var paused = ReadBool(reader, 4); var timestamp = ReadUInt64(reader, 8);
            var telemetryPluginRevision = ReadUInt32(reader, 40);
            var gameVersionMajor = ReadUInt32(reader, 44);
            var gameVersionMinor = ReadUInt32(reader, 48);
            var telemetryGameVersionMajor = ReadUInt32(reader, 56);
            var telemetryGameVersionMinor = ReadUInt32(reader, 60);
            var timeAbsMinutes = ReadUInt32(reader, 64);
            var restStopMinutes = ReadInt32(reader, 500);
            var game = ReadUInt32(reader, Zone2 + 12);
            var plannedDistance = ReadUInt32(reader, Zone2 + 28 + 32);
            var retarderLevel = ReadUInt32(reader, Zone2 + 64 + 4);
            var truckWheelCountFromConfig = ReadUInt32(reader, Zone2 + 28 + 12);
            var selectorCount = ReadUInt32(reader, Zone2 + 28 + 16);
            var maxTrailerCount = ReadUInt32(reader, Zone2 + 28 + 24);
            var unitCount = ReadUInt32(reader, Zone2 + 28 + 28);
            var shifterSlot = ReadUInt32(reader, Zone2 + 64 + 0);
            var retarderBrake = ReadUInt32(reader, Zone2 + 64 + 4);
            var hshifterPosition = new uint[32]; var hshifterBitmask = new uint[32]; var hshifterResulting = new int[32];
            for (var h = 0; h < 32; h++) { hshifterPosition[h] = ReadUInt32(reader, Zone2 + 144 + h * 4); hshifterBitmask[h] = ReadUInt32(reader, Zone2 + 272 + h * 4); hshifterResulting[h] = ReadInt32(reader, Zone3 + 12 + h * 4); }
            var jobDeliveredDeliveryTime = ReadUInt32(reader, Zone2 + 400);
            var jobStartingTime = ReadUInt32(reader, Zone2 + 404);
            var jobFinishedTime = ReadUInt32(reader, Zone2 + 408);
            var jobDeliveredEarnedXp = ReadInt32(reader, Zone3 + 140);
            var lightsAuxFront = ReadUInt32(reader, Zone2 + 64 + 8);
            var lightsAuxRoof = ReadUInt32(reader, Zone2 + 64 + 12);
            var jobIncome = ReadUInt64(reader, JobIncomeOffset);
            const int TruckFloat = Zone4 + 4 + 244;
            var speed = ReadFloat(reader, TruckFloat + 0); var rpm = ReadFloat(reader, TruckFloat + 4); var userThrottle = ReadFloat(reader, TruckFloat + 12); var userBrake = ReadFloat(reader, TruckFloat + 16); var gameThrottle = ReadFloat(reader, TruckFloat + 28); var gameBrake = ReadFloat(reader, TruckFloat + 32); var cruiseSpeed = ReadFloat(reader, TruckFloat + 40); var airPressure = ReadFloat(reader, TruckFloat + 44); var brakeTemperature = ReadFloat(reader, TruckFloat + 48); var fuel = ReadFloat(reader, TruckFloat + 52); var fuelAvgConsumption = ReadFloat(reader, TruckFloat + 56); var fuelRange = ReadFloat(reader, TruckFloat + 60); var adblue = ReadFloat(reader, TruckFloat + 64); var oilPressure = ReadFloat(reader, TruckFloat + 68); var oilTemperature = ReadFloat(reader, TruckFloat + 72); var waterTemperature = ReadFloat(reader, TruckFloat + 76); var batteryVoltage = ReadFloat(reader, TruckFloat + 80);
            var wearEngine = ReadFloat(reader, TruckFloat + 88); var wearTransmission = ReadFloat(reader, TruckFloat + 92); var wearCabin = ReadFloat(reader, TruckFloat + 96); var wearChassis = ReadFloat(reader, TruckFloat + 100); var wearWheels = ReadFloat(reader, TruckFloat + 104); var odometer = ReadFloat(reader, TruckFloat + 108); var routeDistance = ReadFloat(reader, TruckFloat + 112); var routeTime = ReadFloat(reader, TruckFloat + 116); var speedLimit = ReadFloat(reader, TruckFloat + 120);
            var userSteer = ReadFloat(reader, TruckFloat + 8); var userClutch = ReadFloat(reader, TruckFloat + 20); var gameSteer = ReadFloat(reader, TruckFloat + 24); var gameClutch = ReadFloat(reader, TruckFloat + 36);
            var lightsDashboard = ReadFloat(reader, TruckFloat + 84);
            var fuelCapacity = ReadFloat(reader, Zone4 + 4); var fuelWarningFactor = ReadFloat(reader, Zone4 + 8); var adblueCapacity = ReadFloat(reader, Zone4 + 12); var adblueWarningFactor = ReadFloat(reader, Zone4 + 16);
            var airPressureWarningLimit = ReadFloat(reader, Zone4 + 20); var airPressureEmergencyLimit = ReadFloat(reader, Zone4 + 24); var oilPressureWarningLimit = ReadFloat(reader, Zone4 + 28); var waterTemperatureWarningLimit = ReadFloat(reader, Zone4 + 32); var batteryVoltageWarningLimit = ReadFloat(reader, Zone4 + 36);
            var engineRpmMax = ReadFloat(reader, Zone4 + 40); var gearDifferential = ReadFloat(reader, Zone4 + 44);
            var truckWheelRadiusConfig = new float[16];
            for (var wheel = 0; wheel < 16; wheel++) truckWheelRadiusConfig[wheel] = SafeNonNegative(ReadFloat(reader, Zone4 + 48 + wheel * 4));
            var gearRatiosForward = new float[24]; var gearRatiosReverse = new float[8];
            for (var ratio = 0; ratio < 24; ratio++) gearRatiosForward[ratio] = SafeFloat(ReadFloat(reader, Zone4 + 4 + 116 + ratio * 4));
            for (var ratio = 0; ratio < 8; ratio++) gearRatiosReverse[ratio] = SafeFloat(ReadFloat(reader, Zone4 + 4 + 212 + ratio * 4));
            var forwardGearCount = ReadUInt32(reader, Zone2 + 28 + 0);
            var reverseGearCount = ReadUInt32(reader, Zone2 + 28 + 4);
            var retarderStepCount = ReadUInt32(reader, Zone2 + 28 + 8);
            var deliveryTimeAbs = ReadUInt32(reader, Zone2 + 28 + 20);
            var unitMass = SafeNonNegative(ReadFloat(reader, Zone4 + 4 + 240));
            // truck_f has six 16-float wheel arrays after speedLimit. gameplay_f starts at index 127 and job_f.cargoDamage at index 130.
            var cargoDamage = ReadFloat(reader, TruckFloat + (130 * 4)); var refuelAmount = ReadFloat(reader, TruckFloat + (129 * 4));

            // SCS truck wheel telemetry: configuration, state, dynamics and contact/substance.
            var truckWheelCount = (int)Math.Min(16u, truckWheelCountFromConfig);
            var truckWheelRadius = new float[16];
            var truckWheelSuspDeflection = new float[16];
            var truckWheelVelocity = new float[16];
            var truckWheelSteering = new float[16];
            var truckWheelRotation = new float[16];
            var truckWheelLift = new float[16];
            var truckWheelLiftOffset = new float[16];
            var truckWheelSteerable = new bool[16];
            var truckWheelSimulated = new bool[16];
            var truckWheelPowered = new bool[16];
            var truckWheelLiftable = new bool[16];
            var truckWheelOnGround = new bool[16];
            var truckWheelSubstance = new uint[16];

            const int ConfigWheelRadius = Zone4 + 4 + (12 * 4);
            const int WheelSuspension = TruckFloat + (31 * 4);
            const int WheelVelocity = TruckFloat + (47 * 4);
            const int WheelSteering = TruckFloat + (63 * 4);
            const int WheelRotation = TruckFloat + (79 * 4);
            const int WheelLift = TruckFloat + (95 * 4);
            const int WheelLiftOffset = TruckFloat + (111 * 4);
            const int WheelSteerable = Zone5;
            const int WheelSimulated = Zone5 + 16;
            const int WheelPowered = Zone5 + 32;
            const int WheelLiftable = Zone5 + 48;
            const int WheelOnGround = Zone5 + 66 + 24;
            const int WheelSubstance = Zone2 + 64 + 16;

            for (var wheel = 0; wheel < 16; wheel++)
            {
                truckWheelRadius[wheel] = SafeNonNegative(ReadFloat(reader, ConfigWheelRadius + wheel * 4));
                truckWheelSuspDeflection[wheel] = SafeFloat(ReadFloat(reader, WheelSuspension + wheel * 4));
                truckWheelVelocity[wheel] = SafeFloat(ReadFloat(reader, WheelVelocity + wheel * 4));
                truckWheelSteering[wheel] = SafeFloat(ReadFloat(reader, WheelSteering + wheel * 4));
                truckWheelRotation[wheel] = SafeFloat(ReadFloat(reader, WheelRotation + wheel * 4));
                truckWheelLift[wheel] = Clamp01(ReadFloat(reader, WheelLift + wheel * 4));
                truckWheelLiftOffset[wheel] = SafeFloat(ReadFloat(reader, WheelLiftOffset + wheel * 4));
                truckWheelSteerable[wheel] = ReadBool(reader, WheelSteerable + wheel);
                truckWheelSimulated[wheel] = ReadBool(reader, WheelSimulated + wheel);
                truckWheelPowered[wheel] = ReadBool(reader, WheelPowered + wheel);
                truckWheelLiftable[wheel] = ReadBool(reader, WheelLiftable + wheel);
                truckWheelOnGround[wheel] = ReadBool(reader, WheelOnGround + wheel);
                truckWheelSubstance[wheel] = ReadUInt32(reader, WheelSubstance + wheel * 4);
            }
            // SCS shared-memory zone 7 (fplacement): cabin/head offsets and rotations.
            const int Zone7 = 2000;
            var cabinOffsetX = SafeFloat(ReadFloat(reader, Zone7 + 0));
            var cabinOffsetY = SafeFloat(ReadFloat(reader, Zone7 + 4));
            var cabinOffsetZ = SafeFloat(ReadFloat(reader, Zone7 + 8));
            var cabinOffsetRotationX = SafeFloat(ReadFloat(reader, Zone7 + 12));
            var cabinOffsetRotationY = SafeFloat(ReadFloat(reader, Zone7 + 16));
            var cabinOffsetRotationZ = SafeFloat(ReadFloat(reader, Zone7 + 20));
            var headOffsetX = SafeFloat(ReadFloat(reader, Zone7 + 24));
            var headOffsetY = SafeFloat(ReadFloat(reader, Zone7 + 28));
            var headOffsetZ = SafeFloat(ReadFloat(reader, Zone7 + 32));
            var headOffsetRotationX = SafeFloat(ReadFloat(reader, Zone7 + 36));
            var headOffsetRotationY = SafeFloat(ReadFloat(reader, Zone7 + 40));
            var headOffsetRotationZ = SafeFloat(ReadFloat(reader, Zone7 + 44));

            // SCS telemetry shared-memory zone 8 (dplacement): world X/Y/Z + heading/pitch/roll.
            // Orientation is normalized by the SDK to turns (0..1), so expose degrees too.
            var worldX = ReadDouble(reader, Zone8 + 0); var worldY = ReadDouble(reader, Zone8 + 8); var worldZ = ReadDouble(reader, Zone8 + 16);
            var heading = ReadDouble(reader, Zone8 + 24); var pitch = ReadDouble(reader, Zone8 + 32); var roll = ReadDouble(reader, Zone8 + 40);
            var positionValid = IsFinite(worldX) && IsFinite(worldY) && IsFinite(worldZ) && (Math.Abs(worldX) > 0.001 || Math.Abs(worldY) > 0.001 || Math.Abs(worldZ) > 0.001);
            var headingDeg = NormalizeDegrees(heading * 360.0); var pitchDeg = NormalizeDegrees(pitch * 360.0); var rollDeg = NormalizeDegrees(roll * 360.0);
            // SCS shared-memory zone 6 (fvector): motion, cabin/head/hook and wheel positions.
            const int Zone6 = 1640;
            const int TruckVector = Zone6 + 228;
            var localVelocityX = ReadFloat(reader, TruckVector + 0);
            var localVelocityY = ReadFloat(reader, TruckVector + 4);
            var localVelocityZ = ReadFloat(reader, TruckVector + 8);
            var angularVelocityX = ReadFloat(reader, TruckVector + 12);
            var angularVelocityY = ReadFloat(reader, TruckVector + 16);
            var angularVelocityZ = ReadFloat(reader, TruckVector + 20);
            var linearAccelerationX = ReadFloat(reader, TruckVector + 24);
            var linearAccelerationY = ReadFloat(reader, TruckVector + 28);
            var linearAccelerationZ = ReadFloat(reader, TruckVector + 32);
            var angularAccelerationX = ReadFloat(reader, TruckVector + 36);
            var angularAccelerationY = ReadFloat(reader, TruckVector + 40);
            var angularAccelerationZ = ReadFloat(reader, TruckVector + 44);
            var cabinAngularVelocityX = ReadFloat(reader, TruckVector + 48);
            var cabinAngularVelocityY = ReadFloat(reader, TruckVector + 52);
            var cabinAngularVelocityZ = ReadFloat(reader, TruckVector + 56);
            var cabinAngularAccelerationX = ReadFloat(reader, TruckVector + 60);
            var cabinAngularAccelerationY = ReadFloat(reader, TruckVector + 64);
            var cabinAngularAccelerationZ = ReadFloat(reader, TruckVector + 68);

            var cabinPositionX = ReadFloat(reader, Zone6 + 0);
            var cabinPositionY = ReadFloat(reader, Zone6 + 4);
            var cabinPositionZ = ReadFloat(reader, Zone6 + 8);
            var headPositionX = ReadFloat(reader, Zone6 + 12);
            var headPositionY = ReadFloat(reader, Zone6 + 16);
            var headPositionZ = ReadFloat(reader, Zone6 + 20);
            var truckHookPositionX = ReadFloat(reader, Zone6 + 24);
            var truckHookPositionY = ReadFloat(reader, Zone6 + 28);
            var truckHookPositionZ = ReadFloat(reader, Zone6 + 32);

            var truckWheelPositionsX = new float[16];
            var truckWheelPositionsY = new float[16];
            var truckWheelPositionsZ = new float[16];
            for (var wheel = 0; wheel < 16; wheel++)
            {
                truckWheelPositionsX[wheel] = ReadFloat(reader, Zone6 + 36 + wheel * 4);
                truckWheelPositionsY[wheel] = ReadFloat(reader, Zone6 + 100 + wheel * 4);
                truckWheelPositionsZ[wheel] = ReadFloat(reader, Zone6 + 164 + wheel * 4);
            }

            var gear = ReadGear(reader);

            // Trailer zone: up to 10 trailers, 1560 bytes each.
            // Read placement and attachment state without changing existing truck/job logic.
            var trailers = new TrailerTelemetry[10];
            const int TrailerBase = 6000;
            const int TrailerSize = 1560;
            for (var trailerIndex = 0; trailerIndex < trailers.Length; trailerIndex++)
            {
                var baseOffset = TrailerBase + trailerIndex * TrailerSize;
                var attached = ReadBool(reader, baseOffset + 80);
                var trailerX = ReadDouble(reader, baseOffset + 872);
                var trailerY = ReadDouble(reader, baseOffset + 880);
                var trailerZ = ReadDouble(reader, baseOffset + 888);
                var trailerHeading = NormalizeDegrees(ReadDouble(reader, baseOffset + 896) * 360.0);
                var trailerPitch = NormalizeDegrees(ReadDouble(reader, baseOffset + 904) * 360.0);
                var trailerRoll = NormalizeDegrees(ReadDouble(reader, baseOffset + 912) * 360.0);

                var wheelCount = (int)Math.Min(16u, ReadUInt32(reader, baseOffset + 148));
                var wheelSteerable = new bool[16];
                var wheelSimulated = new bool[16];
                var wheelPowered = new bool[16];
                var wheelLiftable = new bool[16];
                var wheelOnGround = new bool[16];
                var wheelSubstance = new uint[16];
                var wheelRadius = new float[16];
                var wheelSuspDeflection = new float[16];
                var wheelVelocity = new float[16];
                var wheelSteering = new float[16];
                var wheelRotation = new float[16];
                var wheelLift = new float[16];
                var wheelLiftOffset = new float[16];
                var wheelPositionX = new float[16];
                var wheelPositionY = new float[16];
                var wheelPositionZ = new float[16];

                for (var wheel = 0; wheel < 16; wheel++)
                {
                    wheelSteerable[wheel] = ReadBool(reader, baseOffset + wheel);
                    wheelSimulated[wheel] = ReadBool(reader, baseOffset + 16 + wheel);
                    wheelPowered[wheel] = ReadBool(reader, baseOffset + 32 + wheel);
                    wheelLiftable[wheel] = ReadBool(reader, baseOffset + 48 + wheel);
                    wheelOnGround[wheel] = ReadBool(reader, baseOffset + 64 + wheel);
                    wheelSubstance[wheel] = ReadUInt32(reader, baseOffset + 84 + wheel * 4);

                    wheelSuspDeflection[wheel] = SafeFloat(ReadFloat(reader, baseOffset + 168 + wheel * 4));
                    wheelVelocity[wheel] = SafeFloat(ReadFloat(reader, baseOffset + 232 + wheel * 4));
                    wheelSteering[wheel] = SafeFloat(ReadFloat(reader, baseOffset + 296 + wheel * 4));
                    wheelRotation[wheel] = SafeFloat(ReadFloat(reader, baseOffset + 360 + wheel * 4));
                    wheelLift[wheel] = Clamp01(ReadFloat(reader, baseOffset + 424 + wheel * 4));
                    wheelLiftOffset[wheel] = SafeFloat(ReadFloat(reader, baseOffset + 488 + wheel * 4));
                    wheelRadius[wheel] = SafeNonNegative(ReadFloat(reader, baseOffset + 552 + wheel * 4));

                    wheelPositionX[wheel] = SafeFloat(ReadFloat(reader, baseOffset + 676 + wheel * 4));
                    wheelPositionY[wheel] = SafeFloat(ReadFloat(reader, baseOffset + 740 + wheel * 4));
                    wheelPositionZ[wheel] = SafeFloat(ReadFloat(reader, baseOffset + 804 + wheel * 4));
                }

                var trailerLinearVelocityX = SafeFloat(ReadFloat(reader, baseOffset + 616));
                var trailerLinearVelocityY = SafeFloat(ReadFloat(reader, baseOffset + 620));
                var trailerLinearVelocityZ = SafeFloat(ReadFloat(reader, baseOffset + 624));
                var trailerAngularVelocityX = SafeFloat(ReadFloat(reader, baseOffset + 628));
                var trailerAngularVelocityY = SafeFloat(ReadFloat(reader, baseOffset + 632));
                var trailerAngularVelocityZ = SafeFloat(ReadFloat(reader, baseOffset + 636));
                var trailerLinearAccelerationX = SafeFloat(ReadFloat(reader, baseOffset + 640));
                var trailerLinearAccelerationY = SafeFloat(ReadFloat(reader, baseOffset + 644));
                var trailerLinearAccelerationZ = SafeFloat(ReadFloat(reader, baseOffset + 648));
                var trailerAngularAccelerationX = SafeFloat(ReadFloat(reader, baseOffset + 652));
                var trailerAngularAccelerationY = SafeFloat(ReadFloat(reader, baseOffset + 656));
                var trailerAngularAccelerationZ = SafeFloat(ReadFloat(reader, baseOffset + 660));
                var trailerHookPositionX = SafeFloat(ReadFloat(reader, baseOffset + 664));
                var trailerHookPositionY = SafeFloat(ReadFloat(reader, baseOffset + 668));
                var trailerHookPositionZ = SafeFloat(ReadFloat(reader, baseOffset + 672));

                var trailerCargoDamage = Clamp01(ReadFloat(reader, baseOffset + 152));
                var trailerWearChassis = Clamp01(ReadFloat(reader, baseOffset + 156));
                var trailerWearWheels = Clamp01(ReadFloat(reader, baseOffset + 160));
                var trailerWearBody = Clamp01(ReadFloat(reader, baseOffset + 164));

                var trailerId = Clean(ReadString(reader, baseOffset + 920));
                var cargoAccessoryId = Clean(ReadString(reader, baseOffset + 984));
                var bodyType = Clean(ReadString(reader, baseOffset + 1048));
                var brandId = Clean(ReadString(reader, baseOffset + 1112));
                var brand = Clean(ReadString(reader, baseOffset + 1176));
                var name = Clean(ReadString(reader, baseOffset + 1240));
                var chainType = Clean(ReadString(reader, baseOffset + 1304));
                var trailerLicensePlate = Clean(ReadString(reader, baseOffset + 1368));
                var trailerLicenseCountry = Clean(ReadString(reader, baseOffset + 1432));
                var trailerLicenseCountryId = Clean(ReadString(reader, baseOffset + 1496));

                trailers[trailerIndex] = new TrailerTelemetry
                {
                    Index = trailerIndex,
                    Attached = attached,
                    WorldX = SafeDouble(trailerX),
                    WorldY = SafeDouble(trailerY),
                    WorldZ = SafeDouble(trailerZ),
                    HeadingDeg = SafeDouble(trailerHeading),
                    PitchDeg = SafeDouble(trailerPitch),
                    RollDeg = SafeDouble(trailerRoll),
                    PositionValid = attached && IsFinite(trailerX) && IsFinite(trailerY) && IsFinite(trailerZ),
                    WheelCount = wheelCount,
                    WheelSteerable = wheelSteerable,
                    WheelSimulated = wheelSimulated,
                    WheelPowered = wheelPowered,
                    WheelLiftable = wheelLiftable,
                    WheelOnGround = wheelOnGround,
                    WheelSubstance = wheelSubstance,
                    WheelRadius = wheelRadius,
                    WheelSuspDeflection = wheelSuspDeflection,
                    WheelVelocity = wheelVelocity,
                    WheelSteering = wheelSteering,
                    WheelRotation = wheelRotation,
                    WheelLift = wheelLift,
                    WheelLiftOffset = wheelLiftOffset,
                    WheelPositionX = wheelPositionX,
                    WheelPositionY = wheelPositionY,
                    WheelPositionZ = wheelPositionZ,
                    LinearVelocityX = trailerLinearVelocityX,
                    LinearVelocityY = trailerLinearVelocityY,
                    LinearVelocityZ = trailerLinearVelocityZ,
                    AngularVelocityX = trailerAngularVelocityX,
                    AngularVelocityY = trailerAngularVelocityY,
                    AngularVelocityZ = trailerAngularVelocityZ,
                    LinearAccelerationX = trailerLinearAccelerationX,
                    LinearAccelerationY = trailerLinearAccelerationY,
                    LinearAccelerationZ = trailerLinearAccelerationZ,
                    AngularAccelerationX = trailerAngularAccelerationX,
                    AngularAccelerationY = trailerAngularAccelerationY,
                    AngularAccelerationZ = trailerAngularAccelerationZ,
                    HookPositionX = trailerHookPositionX,
                    HookPositionY = trailerHookPositionY,
                    HookPositionZ = trailerHookPositionZ,
                    CargoDamage = trailerCargoDamage,
                    WearChassis = trailerWearChassis,
                    WearWheels = trailerWearWheels,
                    WearBody = trailerWearBody,
                    Id = trailerId,
                    CargoAccessoryId = cargoAccessoryId,
                    BodyType = bodyType,
                    BrandId = brandId,
                    Brand = brand,
                    Name = name,
                    ChainType = chainType,
                    LicensePlate = trailerLicensePlate,
                    LicensePlateCountry = trailerLicenseCountry,
                    LicensePlateCountryId = trailerLicenseCountryId
                };
            }

            const int ConfigBool = Zone5, TruckBool = Zone5 + 66;
            var cargoLoaded = ReadBool(reader, ConfigBool + 64); var specialJob = ReadBool(reader, ConfigBool + 65); var parkingBrake = ReadBool(reader, TruckBool + 0); var motorBrake = ReadBool(reader, TruckBool + 1); var airPressureWarning = ReadBool(reader, TruckBool + 2); var airPressureEmergency = ReadBool(reader, TruckBool + 3); var fuelWarning = ReadBool(reader, TruckBool + 4); var adblueWarning = ReadBool(reader, TruckBool + 5); var oilPressureWarning = ReadBool(reader, TruckBool + 6); var waterTemperatureWarning = ReadBool(reader, TruckBool + 7); var batteryVoltageWarning = ReadBool(reader, TruckBool + 8); var electricEnabled = ReadBool(reader, TruckBool + 9); var engineEnabled = ReadBool(reader, TruckBool + 10); var wipers = ReadBool(reader, TruckBool + 11); var blinkerLeftActive = ReadBool(reader, TruckBool + 12); var blinkerRightActive = ReadBool(reader, TruckBool + 13); var blinkerLeftOn = ReadBool(reader, TruckBool + 14); var blinkerRightOn = ReadBool(reader, TruckBool + 15); var lightsParking = ReadBool(reader, TruckBool + 16); var lightsBrake = ReadBool(reader, TruckBool + 20); var lightsReverse = ReadBool(reader, TruckBool + 21); var lightsHazard = ReadBool(reader, TruckBool + 22); var cruiseControl = ReadBool(reader, TruckBool + 23); var differentialLock = ReadBool(reader, TruckBool + 32); var liftAxle = ReadBool(reader, TruckBool + 33); var liftAxleIndicator = ReadBool(reader, TruckBool + 34); var trailerLiftAxle = ReadBool(reader, TruckBool + 35); var trailerLiftAxleIndicator = ReadBool(reader, TruckBool + 36);
            var shifterToggle1 = ReadBool(reader, TruckBool + 40); var shifterToggle2 = ReadBool(reader, TruckBool + 41);
            var jobDeliveredAutoparkUsed = ReadBool(reader, Zone5 + 113); var jobDeliveredAutoloadUsed = ReadBool(reader, Zone5 + 114);
            var onJob = ReadBool(reader, SpecialEventsOffset + 0); var jobFinished = ReadBool(reader, SpecialEventsOffset + 1); var jobCancelled = ReadBool(reader, SpecialEventsOffset + 2); var jobDelivered = ReadBool(reader, SpecialEventsOffset + 3); var fined = ReadBool(reader, SpecialEventsOffset + 4); var tollgate = ReadBool(reader, SpecialEventsOffset + 5); var ferry = ReadBool(reader, SpecialEventsOffset + 6); var train = ReadBool(reader, SpecialEventsOffset + 7); var refuel = ReadBool(reader, SpecialEventsOffset + 8); var refuelPayed = ReadBool(reader, SpecialEventsOffset + 9);
            var jobCancelledPenalty = ReadInt64(reader, 4200); var jobDeliveredRevenue = ReadInt64(reader, 4208); var fineAmount = ReadInt64(reader, 4216); var tollgateAmount = ReadInt64(reader, 4224); var ferryPayAmount = ReadInt64(reader, 4232); var trainPayAmount = ReadInt64(reader, 4240);
            var deliveredCargoDamage = ReadFloat(reader, TruckFloat + 127 * 4); var deliveredDistanceKm = ReadFloat(reader, TruckFloat + 128 * 4);
            var tollgatePaid = tollgate != _lastTollgateFlag && tollgateAmount > 0;
            if (tollgatePaid) { _lastTollgateAmount = tollgateAmount; _tollgateSequence++; _tollgatePulseUntilUtc = DateTime.UtcNow.AddSeconds(3); }
            _lastTollgateFlag = tollgate;
            var truckBrandId = ReadString(reader, Zone9 + 0); var truckBrand = ReadString(reader, Zone9 + 64); var truckId = ReadString(reader, Zone9 + 128); var truckName = ReadString(reader, Zone9 + 192); var cargoId = ReadString(reader, Zone9 + 256); var cargo = ReadString(reader, Zone9 + 320); var destinationCityId = ReadString(reader, Zone9 + 384); var destinationCity = ReadString(reader, Zone9 + 448); var destinationCompanyId = ReadString(reader, Zone9 + 512); var destinationCompany = ReadString(reader, Zone9 + 576); var sourceCityId = ReadString(reader, Zone9 + 640); var sourceCity = ReadString(reader, Zone9 + 704); var sourceCompanyId = ReadString(reader, Zone9 + 768); var sourceCompany = ReadString(reader, Zone9 + 832); var shifterType = ReadString(reader, Zone9 + 896); var licensePlate = ReadString(reader, Zone9 + 912); var licensePlateCountryId = ReadString(reader, Zone9 + 976); var licensePlateCountry = ReadString(reader, Zone9 + 1040); var jobMarket = ReadString(reader, Zone9 + 1104); var fineOffence = ReadString(reader, Zone9 + 1136); var ferrySourceName = ReadString(reader, Zone9 + 1168); var ferryTargetName = ReadString(reader, Zone9 + 1232); var ferrySourceId = ReadString(reader, Zone9 + 1296); var ferryTargetId = ReadString(reader, Zone9 + 1360); var trainSourceName = ReadString(reader, Zone9 + 1424); var trainTargetName = ReadString(reader, Zone9 + 1488); var trainSourceId = ReadString(reader, Zone9 + 1552); var trainTargetId = ReadString(reader, Zone9 + 1616); var cargoMass = ReadFloat(reader, Zone4 + 4 + (11 * 4)); var gameName = game == 1 ? "ETS2" : game == 2 ? "ATS" : "Unknown";
            return new TelemetrySnapshot
            {
                Connected = sdkActive, Updated = sdkActive, Timestamp = timestamp, TelemetryPluginRevision = telemetryPluginRevision, GameVersionMajor = gameVersionMajor, GameVersionMinor = gameVersionMinor, TelemetryGameVersionMajor = telemetryGameVersionMajor, TelemetryGameVersionMinor = telemetryGameVersionMinor, TimeAbsMinutes = timeAbsMinutes, RestStopMinutes = restStopMinutes, Game = gameName, TollgatePaid = DateTime.UtcNow < _tollgatePulseUntilUtc, TollgateAmount = DateTime.UtcNow < _tollgatePulseUntilUtc ? _lastTollgateAmount : 0, TollgateEventId = DateTime.UtcNow < _tollgatePulseUntilUtc ? _tollgateSequence : 0, GamePaused = paused, TruckBrand = Clean(truckBrand), TruckModel = Clean(truckName), TruckId = Clean(truckId), LicensePlate = Clean(licensePlate), EngineEnabled = engineEnabled, ElectricEnabled = electricEnabled, CargoLoaded = cargoLoaded, SpecialJob = specialJob, OnJob = onJob, JobFinished = jobFinished, JobCancelled = jobCancelled, JobDelivered = jobDelivered, RefuelActive = refuel, RefuelPayed = refuelPayed, RefuelAmountLiters = SafeNonNegative(refuelAmount),
                WorldX = SafeDouble(worldX), WorldY = SafeDouble(worldY), WorldZ = SafeDouble(worldZ), HeadingDeg = SafeDouble(headingDeg), PitchDeg = SafeDouble(pitchDeg), RollDeg = SafeDouble(rollDeg), PositionValid = positionValid,
                LocalVelocityX = SafeFloat(localVelocityX), LocalVelocityY = SafeFloat(localVelocityY), LocalVelocityZ = SafeFloat(localVelocityZ),
                AngularVelocityX = SafeFloat(angularVelocityX), AngularVelocityY = SafeFloat(angularVelocityY), AngularVelocityZ = SafeFloat(angularVelocityZ),
                LinearAccelerationX = SafeFloat(linearAccelerationX), LinearAccelerationY = SafeFloat(linearAccelerationY), LinearAccelerationZ = SafeFloat(linearAccelerationZ),
                AngularAccelerationX = SafeFloat(angularAccelerationX), AngularAccelerationY = SafeFloat(angularAccelerationY), AngularAccelerationZ = SafeFloat(angularAccelerationZ),
                CabinAngularVelocityX = SafeFloat(cabinAngularVelocityX), CabinAngularVelocityY = SafeFloat(cabinAngularVelocityY), CabinAngularVelocityZ = SafeFloat(cabinAngularVelocityZ),
                CabinAngularAccelerationX = SafeFloat(cabinAngularAccelerationX), CabinAngularAccelerationY = SafeFloat(cabinAngularAccelerationY), CabinAngularAccelerationZ = SafeFloat(cabinAngularAccelerationZ),
                CabinPositionX = SafeFloat(cabinPositionX), CabinPositionY = SafeFloat(cabinPositionY), CabinPositionZ = SafeFloat(cabinPositionZ),
                CabinOffsetX = cabinOffsetX, CabinOffsetY = cabinOffsetY, CabinOffsetZ = cabinOffsetZ,
                CabinOffsetRotationX = cabinOffsetRotationX, CabinOffsetRotationY = cabinOffsetRotationY, CabinOffsetRotationZ = cabinOffsetRotationZ,
                HeadOffsetX = headOffsetX, HeadOffsetY = headOffsetY, HeadOffsetZ = headOffsetZ,
                HeadOffsetRotationX = headOffsetRotationX, HeadOffsetRotationY = headOffsetRotationY, HeadOffsetRotationZ = headOffsetRotationZ,
                HeadPositionX = SafeFloat(headPositionX), HeadPositionY = SafeFloat(headPositionY), HeadPositionZ = SafeFloat(headPositionZ),
                TruckHookPositionX = SafeFloat(truckHookPositionX), TruckHookPositionY = SafeFloat(truckHookPositionY), TruckHookPositionZ = SafeFloat(truckHookPositionZ),
                TruckWheelPositionsX = truckWheelPositionsX, TruckWheelPositionsY = truckWheelPositionsY, TruckWheelPositionsZ = truckWheelPositionsZ,
                TruckWheelCount = truckWheelCount, TruckWheelRadius = truckWheelRadius, TruckWheelSuspDeflection = truckWheelSuspDeflection,
                TruckWheelVelocity = truckWheelVelocity, TruckWheelSteering = truckWheelSteering, TruckWheelRotation = truckWheelRotation,
                TruckWheelLift = truckWheelLift, TruckWheelLiftOffset = truckWheelLiftOffset,
                TruckWheelSteerable = truckWheelSteerable, TruckWheelSimulated = truckWheelSimulated, TruckWheelPowered = truckWheelPowered,
                TruckWheelLiftable = truckWheelLiftable, TruckWheelOnGround = truckWheelOnGround, TruckWheelSubstance = truckWheelSubstance,
                Trailers = trailers,
                HShifterPosition = hshifterPosition, HShifterBitmask = hshifterBitmask, HShifterResulting = hshifterResulting, JobDeliveredDeliveryTime = jobDeliveredDeliveryTime, JobStartingTime = jobStartingTime, JobFinishedTime = jobFinishedTime, JobDeliveredEarnedXp = jobDeliveredEarnedXp, JobDeliveredAutoparkUsed = jobDeliveredAutoparkUsed, JobDeliveredAutoloadUsed = jobDeliveredAutoloadUsed, TruckBrandId = Clean(truckBrandId), CargoId = Clean(cargoId), DestinationCityId = Clean(destinationCityId), DestinationCompanyId = Clean(destinationCompanyId), SourceCityId = Clean(sourceCityId), SourceCompanyId = Clean(sourceCompanyId), ShifterType = Clean(shifterType), LicensePlateCountryId = Clean(licensePlateCountryId), LicensePlateCountry = Clean(licensePlateCountry), FerrySourceName = Clean(ferrySourceName), FerryTargetName = Clean(ferryTargetName), FerrySourceId = Clean(ferrySourceId), FerryTargetId = Clean(ferryTargetId), TrainSourceName = Clean(trainSourceName), TrainTargetName = Clean(trainTargetName), TrainSourceId = Clean(trainSourceId), TrainTargetId = Clean(trainTargetId),
                SpeedKph = SafeFloat(speed * 3.6f), SpeedMps = SafeFloat(speed), SpeedLimitKph = SafeFloat(speedLimit * 3.6f), Rpm = SafeFloat(rpm), Gear = gear, UserSteer = SafeFloat(userSteer), UserThrottle = Clamp01(userThrottle), UserBrake = Clamp01(userBrake), UserClutch = Clamp01(userClutch), GameSteer = SafeFloat(gameSteer), EffectiveThrottle = Clamp01(gameThrottle), EffectiveBrake = Clamp01(gameBrake), GameClutch = Clamp01(gameClutch), FuelLiters = SafeNonNegative(fuel), FuelAvgConsumption = SafeNonNegative(fuelAvgConsumption), FuelRangeKm = SafeNonNegative(fuelRange), AdBlueLiters = SafeNonNegative(adblue), OilPressure = SafeNonNegative(oilPressure), OilTemperature = SafeFloat(oilTemperature), WaterTemperature = SafeFloat(waterTemperature), BatteryVoltage = SafeNonNegative(batteryVoltage), OdometerKm = SafeNonNegative(odometer), RouteDistanceKm = SafeNonNegative(routeDistance) / 1000f, RouteTimeSeconds = SafeNonNegative(routeTime), CruiseControl = cruiseControl || cruiseSpeed > 0.1f, CruiseSpeedKph = SafeNonNegative(cruiseSpeed * 3.6f), SourceCity = Clean(sourceCity), DestinationCity = Clean(destinationCity), SourceCompany = Clean(sourceCompany), DestinationCompany = Clean(destinationCompany), Cargo = Clean(cargo), CargoMassKg = SafeNonNegative(cargoMass), PlannedDistanceKm = plannedDistance > 0 ? plannedDistance : (uint)Math.Max(0, routeDistance / 1000f), CargoValueBrl = jobIncome > 0 ? jobIncome : (ulong?)null, FuelCapacityLiters = SafeNonNegative(fuelCapacity), FuelWarningFactor = Clamp01(fuelWarningFactor), AdBlueCapacityLiters = SafeNonNegative(adblueCapacity), AdBlueWarningFactor = Clamp01(adblueWarningFactor), AirPressureWarningLimit = SafeNonNegative(airPressureWarningLimit), AirPressureEmergencyLimit = SafeNonNegative(airPressureEmergencyLimit), OilPressureWarningLimit = SafeNonNegative(oilPressureWarningLimit), WaterTemperatureWarningLimit = SafeNonNegative(waterTemperatureWarningLimit), BatteryVoltageWarningLimit = SafeNonNegative(batteryVoltageWarningLimit), EngineRpmMax = SafeNonNegative(engineRpmMax), GearDifferential = SafeNonNegative(gearDifferential), TruckWheelRadiusConfig = truckWheelRadiusConfig, ForwardGearCount = forwardGearCount, ReverseGearCount = reverseGearCount, RetarderStepCount = retarderStepCount, DeliveryTimeAbs = deliveryTimeAbs, UnitMassKg = unitMass, SelectorCount = selectorCount, MaxTrailerCount = maxTrailerCount, UnitCount = unitCount, ShifterSlot = shifterSlot, RetarderBrake = retarderBrake, GearRatiosForward = gearRatiosForward, GearRatiosReverse = gearRatiosReverse, LightsAuxFront = lightsAuxFront, LightsAuxRoof = lightsAuxRoof, LightsDashboard = SafeFloat(lightsDashboard),
                AirPressure = SafeNonNegative(airPressure), BrakeTemperature = SafeFloat(brakeTemperature), MotorBrake = motorBrake, ParkingBrake = parkingBrake, BrakeLight = lightsBrake, AirPressureWarning = airPressureWarning, AirPressureEmergency = airPressureEmergency, FuelWarning = fuelWarning, AdBlueWarning = adblueWarning, OilPressureWarning = oilPressureWarning, WaterTemperatureWarning = waterTemperatureWarning, BatteryVoltageWarning = batteryVoltageWarning, Wipers = wipers, ShifterToggle1 = shifterToggle1, ShifterToggle2 = shifterToggle2, BlinkerLeftActive = blinkerLeftActive, BlinkerRightActive = blinkerRightActive, BlinkerLeftOn = blinkerLeftOn, BlinkerRightOn = blinkerRightOn, LightsParking = lightsParking, LightsBeamLow = ReadBool(reader, TruckBool + 17), LightsBeamHigh = ReadBool(reader, TruckBool + 18), LightsBeacon = ReadBool(reader, TruckBool + 19), LightsBrake = lightsBrake, LightsReverse = lightsReverse, LightsHazard = lightsHazard, DifferentialLock = differentialLock, LiftAxle = liftAxle, LiftAxleIndicator = liftAxleIndicator, TrailerLiftAxle = trailerLiftAxle, TrailerLiftAxleIndicator = trailerLiftAxleIndicator, FerryActive = ferry, TrainActive = train, JobCancelledPenalty = jobCancelledPenalty, JobDeliveredRevenue = jobDeliveredRevenue, FineAmount = fineAmount, TollgatePayAmount = tollgateAmount, FerryPayAmount = ferryPayAmount, TrainPayAmount = trainPayAmount, DeliveredCargoDamage = Clamp01(deliveredCargoDamage), DeliveredDistanceKm = SafeNonNegative(deliveredDistanceKm), JobMarket = Clean(jobMarket), FineOffence = Clean(fineOffence), RetarderLevel = retarderLevel, WearEngine = Clamp01(wearEngine), WearTransmission = Clamp01(wearTransmission), WearCabin = Clamp01(wearCabin), WearChassis = Clamp01(wearChassis), WearWheels = Clamp01(wearWheels), CargoDamage = Clamp01(cargoDamage)
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
        private static float ReadFloat(BinaryReader reader, long offset) { reader.BaseStream.Position = offset; return reader.ReadSingle(); }
        private static double ReadDouble(BinaryReader reader, long offset) { reader.BaseStream.Position = offset; return reader.ReadDouble(); }
        private static bool IsFinite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static double SafeDouble(double value) { return IsFinite(value) ? value : 0d; }
        private static double NormalizeDegrees(double value) { if (!IsFinite(value)) return 0d; value %= 360d; if (value < 0d) value += 360d; return value; }
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
    public sealed class TrailerTelemetry
    {
        public int Index { get; set; }
        public bool Attached { get; set; }
        public double WorldX { get; set; }
        public double WorldY { get; set; }
        public double WorldZ { get; set; }
        public double HeadingDeg { get; set; }
        public double PitchDeg { get; set; }
        public double RollDeg { get; set; }
        public bool PositionValid { get; set; }
        public int WheelCount { get; set; }
        public bool[] WheelSteerable { get; set; } = new bool[16];
        public bool[] WheelSimulated { get; set; } = new bool[16];
        public bool[] WheelPowered { get; set; } = new bool[16];
        public bool[] WheelLiftable { get; set; } = new bool[16];
        public bool[] WheelOnGround { get; set; } = new bool[16];
        public uint[] WheelSubstance { get; set; } = new uint[16];
        public float[] WheelRadius { get; set; } = new float[16];
        public float[] WheelSuspDeflection { get; set; } = new float[16];
        public float[] WheelVelocity { get; set; } = new float[16];
        public float[] WheelSteering { get; set; } = new float[16];
        public float[] WheelRotation { get; set; } = new float[16];
        public float[] WheelLift { get; set; } = new float[16];
        public float[] WheelLiftOffset { get; set; } = new float[16];
        public float[] WheelPositionX { get; set; } = new float[16];
        public float[] WheelPositionY { get; set; } = new float[16];
        public float[] WheelPositionZ { get; set; } = new float[16];
        public float LinearVelocityX { get; set; }
        public float LinearVelocityY { get; set; }
        public float LinearVelocityZ { get; set; }
        public float AngularVelocityX { get; set; }
        public float AngularVelocityY { get; set; }
        public float AngularVelocityZ { get; set; }
        public float LinearAccelerationX { get; set; }
        public float LinearAccelerationY { get; set; }
        public float LinearAccelerationZ { get; set; }
        public float AngularAccelerationX { get; set; }
        public float AngularAccelerationY { get; set; }
        public float AngularAccelerationZ { get; set; }
        public float HookPositionX { get; set; }
        public float HookPositionY { get; set; }
        public float HookPositionZ { get; set; }
        public float CargoDamage { get; set; }
        public float WearChassis { get; set; }
        public float WearWheels { get; set; }
        public float WearBody { get; set; }
        public string Id { get; set; }
        public string CargoAccessoryId { get; set; }
        public string BodyType { get; set; }
        public string BrandId { get; set; }
        public string Brand { get; set; }
        public string Name { get; set; }
        public string ChainType { get; set; }
        public string LicensePlate { get; set; }
        public string LicensePlateCountry { get; set; }
        public string LicensePlateCountryId { get; set; }
    }

    public sealed class TelemetrySnapshot
    {
        public bool Connected { get; set; } public bool Updated { get; set; } public ulong Timestamp { get; set; } public uint TelemetryPluginRevision { get; set; } public uint GameVersionMajor { get; set; } public uint GameVersionMinor { get; set; } public uint TelemetryGameVersionMajor { get; set; } public uint TelemetryGameVersionMinor { get; set; } public uint TimeAbsMinutes { get; set; } public int RestStopMinutes { get; set; }
        public float UserSteer { get; set; } public float UserClutch { get; set; } public float GameSteer { get; set; } public float GameClutch { get; set; } public float LightsDashboard { get; set; }
        public float FuelCapacityLiters { get; set; } public float FuelWarningFactor { get; set; } public float AdBlueCapacityLiters { get; set; } public float AdBlueWarningFactor { get; set; } public float AirPressureWarningLimit { get; set; } public float AirPressureEmergencyLimit { get; set; } public float OilPressureWarningLimit { get; set; } public float WaterTemperatureWarningLimit { get; set; } public float BatteryVoltageWarningLimit { get; set; } public float EngineRpmMax { get; set; } public float GearDifferential { get; set; } public float[] TruckWheelRadiusConfig { get; set; } = new float[16]; public float[] GearRatiosForward { get; set; } = new float[24]; public float[] GearRatiosReverse { get; set; } = new float[8]; public uint ForwardGearCount { get; set; } public uint ReverseGearCount { get; set; } public uint RetarderStepCount { get; set; } public uint DeliveryTimeAbs { get; set; } public float UnitMassKg { get; set; } public uint SelectorCount { get; set; } public uint MaxTrailerCount { get; set; } public uint UnitCount { get; set; } public uint ShifterSlot { get; set; } public uint RetarderBrake { get; set; } public uint LightsAuxFront { get; set; } public uint LightsAuxRoof { get; set; } public string Game { get; set; } public bool TollgatePaid { get; set; } public long TollgateAmount { get; set; } public long TollgateEventId { get; set; } public bool GamePaused { get; set; } public string TruckBrand { get; set; } public string TruckModel { get; set; } public string TruckId { get; set; } public string LicensePlate { get; set; } public bool EngineEnabled { get; set; } public bool ElectricEnabled { get; set; } public bool CargoLoaded { get; set; } public bool SpecialJob { get; set; } public bool OnJob { get; set; } public bool JobFinished { get; set; } public bool JobCancelled { get; set; } public bool JobDelivered { get; set; } public bool RefuelActive { get; set; } public bool RefuelPayed { get; set; } public float RefuelAmountLiters { get; set; }
        public double WorldX { get; set; } public double WorldY { get; set; } public double WorldZ { get; set; } public double HeadingDeg { get; set; } public double PitchDeg { get; set; } public double RollDeg { get; set; } public bool PositionValid { get; set; }
        public float LocalVelocityX { get; set; } public float LocalVelocityY { get; set; } public float LocalVelocityZ { get; set; }
        public float AngularVelocityX { get; set; } public float AngularVelocityY { get; set; } public float AngularVelocityZ { get; set; }
        public float LinearAccelerationX { get; set; } public float LinearAccelerationY { get; set; } public float LinearAccelerationZ { get; set; }
        public float AngularAccelerationX { get; set; } public float AngularAccelerationY { get; set; } public float AngularAccelerationZ { get; set; }
        public float CabinAngularVelocityX { get; set; } public float CabinAngularVelocityY { get; set; } public float CabinAngularVelocityZ { get; set; }
        public float CabinAngularAccelerationX { get; set; } public float CabinAngularAccelerationY { get; set; } public float CabinAngularAccelerationZ { get; set; }
        public float CabinPositionX { get; set; } public float CabinPositionY { get; set; } public float CabinPositionZ { get; set; }
        public float CabinOffsetX { get; set; } public float CabinOffsetY { get; set; } public float CabinOffsetZ { get; set; }
        public float CabinOffsetRotationX { get; set; } public float CabinOffsetRotationY { get; set; } public float CabinOffsetRotationZ { get; set; }
        public float HeadOffsetX { get; set; } public float HeadOffsetY { get; set; } public float HeadOffsetZ { get; set; }
        public float HeadOffsetRotationX { get; set; } public float HeadOffsetRotationY { get; set; } public float HeadOffsetRotationZ { get; set; }
        public float HeadPositionX { get; set; } public float HeadPositionY { get; set; } public float HeadPositionZ { get; set; }
        public float TruckHookPositionX { get; set; } public float TruckHookPositionY { get; set; } public float TruckHookPositionZ { get; set; }
        public float[] TruckWheelPositionsX { get; set; } public float[] TruckWheelPositionsY { get; set; } public float[] TruckWheelPositionsZ { get; set; }
        public int TruckWheelCount { get; set; }
        public float[] TruckWheelRadius { get; set; } = new float[16];
        public float[] TruckWheelSuspDeflection { get; set; } = new float[16];
        public float[] TruckWheelVelocity { get; set; } = new float[16];
        public float[] TruckWheelSteering { get; set; } = new float[16];
        public float[] TruckWheelRotation { get; set; } = new float[16];
        public float[] TruckWheelLift { get; set; } = new float[16];
        public float[] TruckWheelLiftOffset { get; set; } = new float[16];
        public bool[] TruckWheelSteerable { get; set; } = new bool[16];
        public bool[] TruckWheelSimulated { get; set; } = new bool[16];
        public bool[] TruckWheelPowered { get; set; } = new bool[16];
        public bool[] TruckWheelLiftable { get; set; } = new bool[16];
        public bool[] TruckWheelOnGround { get; set; } = new bool[16];
        public uint[] TruckWheelSubstance { get; set; } = new uint[16];
        public TrailerTelemetry[] Trailers { get; set; }
        public float SpeedKph { get; set; } public float SpeedMps { get; set; } public float SpeedLimitKph { get; set; } public float Rpm { get; set; } public int Gear { get; set; } public float UserThrottle { get; set; } public float EffectiveThrottle { get; set; } public float UserBrake { get; set; } public float EffectiveBrake { get; set; } public float FuelLiters { get; set; } public float FuelAvgConsumption { get; set; } public float FuelRangeKm { get; set; } public float AdBlueLiters { get; set; } public float OilPressure { get; set; } public float OilTemperature { get; set; } public float WaterTemperature { get; set; } public float BatteryVoltage { get; set; } public float OdometerKm { get; set; } public float RouteDistanceKm { get; set; } public float RouteTimeSeconds { get; set; } public bool CruiseControl { get; set; } public float CruiseSpeedKph { get; set; }
        public string SourceCity { get; set; } public string DestinationCity { get; set; } public string SourceCompany { get; set; } public string DestinationCompany { get; set; } public string Cargo { get; set; } public float CargoMassKg { get; set; } public uint PlannedDistanceKm { get; set; } public ulong? CargoValueBrl { get; set; } public float AirPressure { get; set; } public float BrakeTemperature { get; set; } public bool MotorBrake { get; set; } public bool ParkingBrake { get; set; } public bool BrakeLight { get; set; } public bool AirPressureWarning { get; set; } public bool AirPressureEmergency { get; set; } public bool FuelWarning { get; set; } public bool AdBlueWarning { get; set; } public bool OilPressureWarning { get; set; } public bool WaterTemperatureWarning { get; set; } public bool BatteryVoltageWarning { get; set; }
        public uint[] HShifterPosition { get; set; } = new uint[32]; public uint[] HShifterBitmask { get; set; } = new uint[32]; public int[] HShifterResulting { get; set; } = new int[32]; public uint JobDeliveredDeliveryTime { get; set; } public uint JobStartingTime { get; set; } public uint JobFinishedTime { get; set; } public int JobDeliveredEarnedXp { get; set; } public bool JobDeliveredAutoparkUsed { get; set; } public bool JobDeliveredAutoloadUsed { get; set; }
        public string TruckBrandId { get; set; } public string CargoId { get; set; } public string DestinationCityId { get; set; } public string DestinationCompanyId { get; set; } public string SourceCityId { get; set; } public string SourceCompanyId { get; set; } public string ShifterType { get; set; } public string LicensePlateCountryId { get; set; } public string LicensePlateCountry { get; set; } public string FerrySourceName { get; set; } public string FerryTargetName { get; set; } public string FerrySourceId { get; set; } public string FerryTargetId { get; set; } public string TrainSourceName { get; set; } public string TrainTargetName { get; set; } public string TrainSourceId { get; set; } public string TrainTargetId { get; set; }
        public bool Wipers { get; set; } public bool ShifterToggle1 { get; set; } public bool ShifterToggle2 { get; set; } public bool LightsBeamLow { get; set; } public bool LightsBeamHigh { get; set; } public bool LightsBeacon { get; set; } public bool BlinkerLeftActive { get; set; } public bool BlinkerRightActive { get; set; } public bool BlinkerLeftOn { get; set; } public bool BlinkerRightOn { get; set; } public bool LightsParking { get; set; } public bool LightsBrake { get; set; } public bool LightsReverse { get; set; } public bool LightsHazard { get; set; } public bool DifferentialLock { get; set; } public bool LiftAxle { get; set; } public bool LiftAxleIndicator { get; set; } public bool TrailerLiftAxle { get; set; } public bool TrailerLiftAxleIndicator { get; set; } public bool FerryActive { get; set; } public bool TrainActive { get; set; } public long JobCancelledPenalty { get; set; } public long JobDeliveredRevenue { get; set; } public long FineAmount { get; set; } public long TollgatePayAmount { get; set; } public long FerryPayAmount { get; set; } public long TrainPayAmount { get; set; } public float DeliveredCargoDamage { get; set; } public float DeliveredDistanceKm { get; set; } public string? JobMarket { get; set; } public string? FineOffence { get; set; }
    public uint RetarderLevel { get; set; } public float WearEngine { get; set; } public float WearTransmission { get; set; } public float WearCabin { get; set; } public float WearChassis { get; set; } public float WearWheels { get; set; } public float CargoDamage { get; set; }
        public static TelemetrySnapshot Disconnected() { return new TelemetrySnapshot { Connected = false, Updated = false, Game = "Unknown", CargoLoaded = false }; }
    }
}
