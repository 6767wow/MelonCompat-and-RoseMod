#include "mono_host.h"

#include "native_log.h"

#include <thread>
#include <windows.h>

namespace
{
    using mono_domain_get_fn = void* (*)();
    using mono_thread_attach_fn = void* (*)(void* domain);
    using mono_domain_assembly_open_fn = void* (*)(void* domain, const char* path);
    using mono_assembly_get_image_fn = void* (*)(void* assembly);
    using mono_class_from_name_fn = void* (*)(void* image, const char* nameSpace, const char* name);
    using mono_class_get_method_from_name_fn = void* (*)(void* klass, const char* name, int paramCount);
    using mono_runtime_invoke_fn = void* (*)(void* method, void* obj, void** params, void** exc);

    HMODULE WaitForMonoModule()
    {
        for (auto i = 0; i < 300; i++)
        {
            if (auto module = GetModuleHandleW(L"mono-2.0-bdwgc.dll"))
                return module;
            if (auto module = GetModuleHandleW(L"mono.dll"))
                return module;
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
        }

        return nullptr;
    }
}

namespace rosemod
{
    bool StartMonoHost(const RuntimePaths& paths)
    {
        const auto mono = WaitForMonoModule();
        if (!mono)
        {
            NativeLog::Error(L"Unity Mono runtime was not loaded; cannot start Mono RoseMod host.");
            return false;
        }

        const auto monoGetRootDomain = reinterpret_cast<mono_domain_get_fn>(GetProcAddress(mono, "mono_get_root_domain"));
        const auto monoThreadAttach = reinterpret_cast<mono_thread_attach_fn>(GetProcAddress(mono, "mono_thread_attach"));
        const auto monoDomainAssemblyOpen = reinterpret_cast<mono_domain_assembly_open_fn>(GetProcAddress(mono, "mono_domain_assembly_open"));
        const auto monoAssemblyGetImage = reinterpret_cast<mono_assembly_get_image_fn>(GetProcAddress(mono, "mono_assembly_get_image"));
        const auto monoClassFromName = reinterpret_cast<mono_class_from_name_fn>(GetProcAddress(mono, "mono_class_from_name"));
        const auto monoClassGetMethodFromName = reinterpret_cast<mono_class_get_method_from_name_fn>(GetProcAddress(mono, "mono_class_get_method_from_name"));
        const auto monoRuntimeInvoke = reinterpret_cast<mono_runtime_invoke_fn>(GetProcAddress(mono, "mono_runtime_invoke"));
        if (!monoGetRootDomain || !monoThreadAttach || !monoDomainAssemblyOpen || !monoAssemblyGetImage
            || !monoClassFromName || !monoClassGetMethodFromName || !monoRuntimeInvoke)
        {
            NativeLog::Error(L"Required Mono embedding exports were not found.");
            return false;
        }

        auto domain = monoGetRootDomain();
        if (!domain)
        {
            NativeLog::Error(L"mono_get_root_domain returned null.");
            return false;
        }

        monoThreadAttach(domain);

        const auto assemblyPath = ToUtf8((paths.coreDirectory / L"RoseMod.Core.dll").wstring());
        auto assembly = monoDomainAssemblyOpen(domain, assemblyPath.c_str());
        if (!assembly)
        {
            NativeLog::Error(L"Mono could not open RoseMod.Core.dll.");
            return false;
        }

        auto image = monoAssemblyGetImage(assembly);
        auto klass = image ? monoClassFromName(image, "RoseMod", "RoseModEntrypoint") : nullptr;
        auto method = klass ? monoClassGetMethodFromName(klass, "StartFromNativeMono", 0) : nullptr;
        if (!method)
        {
            NativeLog::Error(L"Mono could not find RoseMod.RoseModEntrypoint.StartFromNativeMono.");
            return false;
        }

        void* exception = nullptr;
        NativeLog::Info(L"Unity Mono host found; entering managed RoseMod compatibility host.");
        monoRuntimeInvoke(method, nullptr, nullptr, &exception);
        if (exception)
        {
            NativeLog::Error(L"Managed RoseMod host threw an exception during Mono startup.");
            return false;
        }

        return true;
    }
}
