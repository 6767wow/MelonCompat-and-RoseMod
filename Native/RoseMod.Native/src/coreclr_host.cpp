#include "coreclr_host.h"

#include "native_log.h"

#include <filesystem>
#include <string>
#include <vector>
#include <windows.h>

namespace
{
    using coreclr_initialize_fn = int(STDMETHODCALLTYPE*)(
        const char* exePath,
        const char* appDomainFriendlyName,
        int propertyCount,
        const char** propertyKeys,
        const char** propertyValues,
        void** hostHandle,
        unsigned int* domainId);

    using coreclr_create_delegate_fn = int(STDMETHODCALLTYPE*)(
        void* hostHandle,
        unsigned int domainId,
        const char* entryPointAssemblyName,
        const char* entryPointTypeName,
        const char* entryPointMethodName,
        void** delegate);

    using start_from_native_fn = int(STDMETHODCALLTYPE*)(const wchar_t* gameRoot);

    void AddDllsToTpa(const std::filesystem::path& directory, std::wstring& tpa)
    {
        if (!std::filesystem::is_directory(directory))
            return;

        for (const auto& entry : std::filesystem::directory_iterator(directory))
        {
            if (!entry.is_regular_file() || entry.path().extension() != L".dll")
                continue;

            if (!tpa.empty())
                tpa.push_back(L';');
            tpa += entry.path().wstring();
        }
    }
}

namespace rosemod
{
    bool StartCoreClrHost(const RuntimePaths& paths)
    {
        const auto coreclrPath = paths.dotnetDirectory / L"coreclr.dll";
        if (!std::filesystem::exists(coreclrPath))
        {
            NativeLog::Error(L"CoreCLR runtime was not found at " + coreclrPath.wstring());
            return false;
        }

        const auto roseModCore = paths.coreDirectory / L"RoseMod.Core.dll";
        if (!std::filesystem::exists(roseModCore))
        {
            NativeLog::Error(L"RoseMod.Core.dll was not found at " + roseModCore.wstring());
            return false;
        }

        auto coreclr = LoadLibraryW(coreclrPath.c_str());
        if (!coreclr)
        {
            NativeLog::Error(L"Failed to load coreclr.dll.");
            return false;
        }

        const auto initialize = reinterpret_cast<coreclr_initialize_fn>(GetProcAddress(coreclr, "coreclr_initialize"));
        const auto createDelegate = reinterpret_cast<coreclr_create_delegate_fn>(GetProcAddress(coreclr, "coreclr_create_delegate"));
        if (!initialize || !createDelegate)
        {
            NativeLog::Error(L"coreclr_initialize/coreclr_create_delegate exports were not found.");
            return false;
        }

        std::wstring tpa;
        AddDllsToTpa(paths.dotnetDirectory, tpa);
        AddDllsToTpa(paths.coreDirectory, tpa);
        AddDllsToTpa(paths.roseModRoot / L"UserLibs", tpa);
        AddDllsToTpa(paths.roseModRoot / L"interop", tpa);

        const auto appPaths = JoinForProperty({
            paths.coreDirectory,
            paths.roseModRoot / L"UserLibs",
            paths.roseModRoot / L"interop",
            paths.roseModRoot / L"Il2CppAssemblies",
            paths.dataDirectory / L"Managed"
        });
        const auto nativePaths = JoinForProperty({
            paths.gameRoot,
            paths.coreDirectory,
            paths.dotnetDirectory,
            paths.roseModRoot / L"UserLibs"
        });

        const std::string exePath = ToUtf8(paths.gameExe.wstring());
        const std::string tpaUtf8 = ToUtf8(tpa);
        const std::string appPathsUtf8 = ToUtf8(appPaths);
        const std::string nativePathsUtf8 = ToUtf8(nativePaths);

        const char* keys[] = {
            "TRUSTED_PLATFORM_ASSEMBLIES",
            "APP_PATHS",
            "APP_NI_PATHS",
            "NATIVE_DLL_SEARCH_DIRECTORIES"
        };
        const char* values[] = {
            tpaUtf8.c_str(),
            appPathsUtf8.c_str(),
            appPathsUtf8.c_str(),
            nativePathsUtf8.c_str()
        };

        void* hostHandle = nullptr;
        unsigned int domainId = 0;
        const auto hr = initialize(
            exePath.c_str(),
            "RoseMod.Native.CoreCLR",
            static_cast<int>(_countof(keys)),
            keys,
            values,
            &hostHandle,
            &domainId);

        if (hr < 0 || !hostHandle)
        {
            NativeLog::Error(L"coreclr_initialize failed with HRESULT " + std::to_wstring(hr));
            return false;
        }

        void* startDelegate = nullptr;
        const auto delegateHr = createDelegate(
            hostHandle,
            domainId,
            "RoseMod.Core",
            "RoseMod.RoseModEntrypoint",
            "StartFromNative",
            &startDelegate);

        if (delegateHr < 0 || !startDelegate)
        {
            NativeLog::Error(L"coreclr_create_delegate failed with HRESULT " + std::to_wstring(delegateHr));
            return false;
        }

        NativeLog::Info(L"CoreCLR host initialized; entering managed RoseMod compatibility host.");
        const auto start = reinterpret_cast<start_from_native_fn>(startDelegate);
        const auto result = start(paths.gameRoot.c_str());
        if (result != 0)
        {
            NativeLog::Error(L"Managed RoseMod host returned " + std::to_wstring(result));
            return false;
        }

        return true;
    }
}
