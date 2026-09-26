using System;
using System.IO.MemoryMappedFiles;

namespace TransPoli;

/// <summary>
/// Central policy-to-IPC bridge for vehicle authorization.
/// Business rules stay in the Desktop; the native DLL only receives a lock request
/// and remains responsible for the physical SAFE_STOP safety gate.
/// </summary>
internal sealed class VehicleAuthorizationManager : IDisposable
{
    private const string MappingName = "TransPoliVehicleControl";
    private const long MappingSize = 20;
    private const uint Magic = 0x54505643; // TPVC
    private const uint ProtocolVersion = 2;

    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _view;
    private bool _disposed;

    public VehicleAuthorizationManager()
    {
        try
        {
            _map = MemoryMappedFile.CreateOrOpen(MappingName, MappingSize, MemoryMappedFileAccess.ReadWrite);
            _view = _map.CreateViewAccessor(0, MappingSize, MemoryMappedFileAccess.ReadWrite);
            InitializeProtocol();
        }
        catch
        {
            // Strict fail-open: IPC availability must never prevent the Desktop
            // from starting or create an alternate blocking mechanism.
            try { _view?.Dispose(); } catch { }
            try { _map?.Dispose(); } catch { }
            _view = null;
            _map = null;
        }
    }

    public VehicleAuthorizationSnapshot Snapshot
    {
        get
        {
            if (_disposed || _view is null) return new(false, VehicleAuthorizationPhysicalState.Authorized, 0, CurrentReason, CurrentSource, RequestedAtUtc);
            try
            {
                return new(
                    _view.ReadInt32(8) != 0,
                    (VehicleAuthorizationPhysicalState)_view.ReadInt32(12),
                    _view.ReadSingle(16),
                    CurrentReason,
                    CurrentSource,
                    RequestedAtUtc);
            }
            catch
            {
                return new(false, VehicleAuthorizationPhysicalState.Authorized, 0, CurrentReason, CurrentSource, RequestedAtUtc);
            }
        }
    }

    public string CurrentReason { get; private set; } = "";
    public string CurrentSource { get; private set; } = "";
    public DateTime? RequestedAtUtc { get; private set; }

    public bool RequestLock(string reason, string source)
    {
        if (_disposed || _view is null) return false;
        try
        {
            CurrentReason = reason?.Trim() ?? "";
            CurrentSource = source?.Trim() ?? "";
            RequestedAtUtc = DateTime.UtcNow;
            _view.Write(8, 1);
            _view.Flush();
            return true;
        }
        catch
        {
            // LAB policy is fail-open: an IPC failure must not create a second
            // blocking path outside the native DLL safety gate.
            return false;
        }
    }

    public bool Authorize(string source)
    {
        if (_disposed || _view is null) return false;
        try
        {
            _view.Write(8, 0);
            _view.Flush();
            CurrentReason = "";
            CurrentSource = source?.Trim() ?? "";
            RequestedAtUtc = null;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void InitializeProtocol()
    {
        if (_view is null) return;
        _view.Write(0, Magic);
        _view.Write(4, ProtocolVersion);
        _view.Write(8, 0);
        _view.Write(12, (int)VehicleAuthorizationPhysicalState.Authorized);
        _view.Write(16, 0f);
        _view.Flush();
    }

    public void Dispose()
    {
        if (_disposed) return;
        // Releasing the Desktop side removes the request owner. The DLL is already
        // fail-open when the mapping/controller disappears.
        try { Authorize("desktop-shutdown"); } catch { }
        _disposed = true;
        _view?.Dispose();
        _map?.Dispose();
    }
}

internal enum VehicleAuthorizationPhysicalState
{
    Authorized = 0,
    LockPending = 1,
    SafeStop = 2,
    Locked = 3,
    UnlockRelease = 4
}

internal sealed record VehicleAuthorizationSnapshot(
    bool LockRequested,
    VehicleAuthorizationPhysicalState PhysicalState,
    float SpeedMps,
    string Reason,
    string Source,
    DateTime? RequestedAtUtc);
