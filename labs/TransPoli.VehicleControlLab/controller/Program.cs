using System.IO.MemoryMappedFiles;

const string MappingName = "TransPoliVehicleControlLab";
const long MappingSize = 20;
const uint Magic = 0x54505643; // TPVC
const uint ProtocolVersion = 2;

using var map = MemoryMappedFile.CreateOrOpen(
    MappingName, MappingSize, MemoryMappedFileAccess.ReadWrite);
using var view = map.CreateViewAccessor(
    0, MappingSize, MemoryMappedFileAccess.ReadWrite);

string StateName(int state) => state switch
{
    0 => "AUTHORIZED",
    1 => "LOCK_PENDING",
    2 => "SAFE_STOP",
    3 => "LOCKED",
    4 => "UNLOCK_RELEASE",
    _ => $"UNKNOWN({state})"
};

void Initialize()
{
    view.Write(0, Magic);
    view.Write(4, ProtocolVersion);
    view.Write(8, 0);  // lock_requested
    view.Write(12, 0); // actual_state
    view.Write(16, 0f);// speed_mps
    view.Flush();
}

void RequestLock(bool requested)
{
    view.Write(8, requested ? 1 : 0);
    view.Flush();
    Console.WriteLine(requested
        ? "LOCK REQUESTED. V3 will wait for SAFE_STOP before applying LOCK."
        : "UNLOCK REQUESTED.");
}

void PrintStatus()
{
    var requested = view.ReadInt32(8) != 0;
    var state = view.ReadInt32(12);
    var speed = view.ReadSingle(16);
    Console.WriteLine($"Requested={(requested ? "LOCK" : "UNLOCK")} | State={StateName(state)} | Speed={speed * 3.6f:F1} km/h");
}

Initialize();

Console.WriteLine("TransPoli VehicleControlLab Controller V3");
Console.WriteLine("R = REQUEST LOCK | U = UNLOCK | S = STATUS | Q = UNLOCK + EXIT");
Console.WriteLine("V3 safety gate: a lock request while moving must remain LOCK_PENDING.");
Console.WriteLine("LAB ONLY. The final TransPoli app will issue policy requests without keyboard shortcuts.");

while (true)
{
    switch (Console.ReadKey(intercept: true).Key)
    {
        case ConsoleKey.R:
            RequestLock(true);
            break;
        case ConsoleKey.U:
            RequestLock(false);
            break;
        case ConsoleKey.S:
            PrintStatus();
            break;
        case ConsoleKey.Q:
            RequestLock(false);
            return;
    }
}
