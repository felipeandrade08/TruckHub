using System.IO.MemoryMappedFiles;

const string MappingName = "TransPoliVehicleControlLab";
const long MappingSize = 12;
const uint Magic = 0x54505643; // TPVC
const uint ProtocolVersion = 1;

using var map = MemoryMappedFile.CreateOrOpen(
    MappingName, MappingSize, MemoryMappedFileAccess.ReadWrite);
using var view = map.CreateViewAccessor(
    0, MappingSize, MemoryMappedFileAccess.ReadWrite);

void SetLocked(bool locked)
{
    view.Write(0, Magic);
    view.Write(4, ProtocolVersion);
    view.Write(8, locked ? 1 : 0);
    view.Flush();
    Console.WriteLine(locked
        ? "LOCK requested. Test only with the truck fully stopped."
        : "UNLOCK requested.");
}

SetLocked(false);

Console.WriteLine("TransPoli VehicleControlLab Controller");
Console.WriteLine("L = LOCK | U = UNLOCK | Q = UNLOCK + EXIT");
Console.WriteLine("LAB ONLY. Do not issue LOCK while the truck is moving.");

while (true)
{
    switch (Console.ReadKey(intercept: true).Key)
    {
        case ConsoleKey.L:
            SetLocked(true);
            break;
        case ConsoleKey.U:
            SetLocked(false);
            break;
        case ConsoleKey.Q:
            SetLocked(false);
            return;
    }
}
