#include "winhttp_proxy.h"

#include "native_log.h"

#include <filesystem>
#include <windows.h>

extern "C" void WINAPI RoseModMissingWinHttpExport()
{
    SetLastError(ERROR_PROC_NOT_FOUND);
}

#define ROSEMOD_WINHTTP_EXPORT(name) extern "C" void* g_##name = reinterpret_cast<void*>(&RoseModMissingWinHttpExport);
#include "winhttp_export_list.h"
#undef ROSEMOD_WINHTTP_EXPORT

namespace
{
    HMODULE realWinHttp = nullptr;

    std::filesystem::path SystemWinHttpPath()
    {
        std::wstring systemDirectory(MAX_PATH, L'\0');
        const auto length = GetSystemDirectoryW(systemDirectory.data(), static_cast<UINT>(systemDirectory.size()));
        systemDirectory.resize(length);
        return std::filesystem::path(systemDirectory) / L"winhttp.dll";
    }

    void ResolveOne(const char* name, void** target)
    {
        if (!realWinHttp)
            return;

        if (auto proc = GetProcAddress(realWinHttp, name))
            *target = reinterpret_cast<void*>(proc);
    }
}

namespace rosemod
{
    void InitializeWinHttpProxy()
    {
        if (realWinHttp)
            return;

        const auto path = SystemWinHttpPath();
        realWinHttp = LoadLibraryW(path.c_str());
        if (!realWinHttp)
        {
            NativeLog::Error(L"Failed to load system winhttp.dll from " + path.wstring());
            return;
        }

#define ROSEMOD_WINHTTP_EXPORT(name) ResolveOne(#name, &g_##name);
#include "winhttp_export_list.h"
#undef ROSEMOD_WINHTTP_EXPORT
        NativeLog::Info(L"System winhttp.dll proxy initialized.");
    }
}
