#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <string>

static bool TransPoliRunning()
{
    PROCESSENTRY32W entry{ sizeof(entry) };
    HANDLE snapshot=CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS,0);
    if(snapshot==INVALID_HANDLE_VALUE) return false;
    bool found=false;
    if(Process32FirstW(snapshot,&entry))
        do {
            if(_wcsicmp(entry.szExeFile,L"TransPoli.exe")==0){ found=true; break; }
        } while(Process32NextW(snapshot,&entry));
    CloseHandle(snapshot);
    return found;
}

static void StartTransPoli()
{
    if(TransPoliRunning()) return;
    wchar_t localAppData[MAX_PATH]{};
    DWORD n=GetEnvironmentVariableW(L"LOCALAPPDATA",localAppData,MAX_PATH);
    if(n==0 || n>=MAX_PATH) return;
    std::wstring exe=std::wstring(localAppData)+L"\\TransPoli\\TransPoli.exe";
    if(GetFileAttributesW(exe.c_str())==INVALID_FILE_ATTRIBUTES) return;

    STARTUPINFOW si{ sizeof(si) };
    PROCESS_INFORMATION pi{};
    std::wstring command=L"\""+exe+L"\" --ets2-autostart";
    if(CreateProcessW(exe.c_str(),command.data(),nullptr,nullptr,FALSE,0,nullptr,nullptr,&si,&pi)){
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
    }
}

extern "C" __declspec(dllexport) unsigned int scs_telemetry_init(unsigned int,const void*,const void*)
{
    StartTransPoli();
    return 0;
}
extern "C" __declspec(dllexport) void scs_telemetry_shutdown(void) {}

BOOL APIENTRY DllMain(HMODULE,DWORD,LPVOID){ return TRUE; }
