# TransPoli VehicleControlLab

Experimental ETS2 input-control laboratory. This branch is isolated from TransPoli v1.0.32.

## Goal

Prove, in the current ETS2 build, which SCS Input SDK semantical controls can enforce a safe vehicle immobilization without process-memory patches.

Initial candidates:
- ignitionoff
- ignitionon
- ignitionstrt
- aforward

## Safety contract

- Default state is UNLOCKED.
- Loss of the controller/IPC must fail open.
- Never request immobilization while the truck is moving.
- No DANFE, refuel, Banco, API or production rules belong in this DLL.
- No ReadProcessMemory/WriteProcessMemory or game offsets.
- Production integration is forbidden until the laboratory result is repeatable.

## Test sequence

1. Confirm the DLL loads under bin/win_x64/plugins.
2. Confirm the semantical input device registers.
3. With the truck stopped, test ignition-off behavior.
4. Test whether physical ignition/start input can override the semantic device.
5. Test whether aforward=0 can suppress a physical accelerator input.
6. Test keyboard, controller and steering wheel separately.
7. Restart ETS2 and verify no control remains latched.
8. Only after these tests, design AUTHORIZED / LOCK_PENDING / LOCKED states.

The lab must remain independent of the stable TransPoli application until these behaviors are proven.
