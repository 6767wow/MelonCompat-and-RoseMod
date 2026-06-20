#include "coreclr_host.h"
#include "mono_host.h"
#include "native_log.h"
#include "paths.h"
#include "winhttp_proxy.h"

#include <thread>
#include <windows.h>

namespace
{
    HMODULE currentModule = nullptr;

    DWORD WINAPI RoseModStartupThread(void*)
    {
        const auto paths = rosemod::DetectRuntimePaths(currentModule);
        rosemod::NativeLog::Initialize(paths.gameRoot);
        rosemod::NativeLog::Banner();
        rosemod::NativeLog::Info(L"Game root: " + paths.gameRoot.wstring());
        rosemod::NativeLog::Info(L"Backend: " + rosemod::BackendName(paths.backend));
        rosemod::InitializeWinHttpProxy();

        bool started = false;
        switch (paths.backend)
        {
        case rosemod::UnityBackend::Il2Cpp:
            started = rosemod::StartCoreClrHost(paths);
            break;
        case rosemod::UnityBackend::Mono:
            started = rosemod::StartMonoHost(paths);
            break;
        default:
            rosemod::NativeLog::Error(L"Unity backend could not be detected.");
            break;
        }

        rosemod::NativeLog::Info(started ? L"RoseMod native startup completed." : L"RoseMod native startup failed.");
        return started ? 0 : 1;
    }
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        currentModule = module;
        DisableThreadLibraryCalls(module);
        rosemod::InitializeWinHttpProxy();

        const auto thread = CreateThread(nullptr, 0, RoseModStartupThread, nullptr, 0, nullptr);
        if (thread)
            CloseHandle(thread);
    }

    return TRUE;
}
