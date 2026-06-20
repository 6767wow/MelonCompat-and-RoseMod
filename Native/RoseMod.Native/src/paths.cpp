#include "paths.h"

#include <algorithm>
#include <sstream>
#include <windows.h>

namespace
{
    std::filesystem::path GetModulePath(HMODULE module)
    {
        std::wstring buffer(MAX_PATH, L'\0');
        for (;;)
        {
            const auto length = GetModuleFileNameW(module, buffer.data(), static_cast<DWORD>(buffer.size()));
            if (length == 0)
                return {};
            if (length < buffer.size() - 1)
            {
                buffer.resize(length);
                return buffer;
            }
            buffer.resize(buffer.size() * 2);
        }
    }

    std::filesystem::path FindDataDirectory(const std::filesystem::path& root, const std::filesystem::path& exe)
    {
        if (!exe.empty())
        {
            auto candidate = root / (exe.stem().wstring() + L"_Data");
            if (std::filesystem::is_directory(candidate))
                return candidate;
        }

        for (const auto& entry : std::filesystem::directory_iterator(root))
        {
            if (!entry.is_directory())
                continue;

            const auto name = entry.path().filename().wstring();
            if (name.size() >= 5 && name.substr(name.size() - 5) == L"_Data")
                return entry.path();
        }

        return {};
    }
}

namespace rosemod
{
    RuntimePaths DetectRuntimePaths(void* moduleHandle)
    {
        RuntimePaths paths{};
        paths.modulePath = GetModulePath(static_cast<HMODULE>(moduleHandle));
        paths.gameRoot = paths.modulePath.parent_path();
        paths.gameExe = GetModulePath(nullptr);
        paths.dataDirectory = FindDataDirectory(paths.gameRoot, paths.gameExe);
        paths.roseModRoot = paths.gameRoot / L"RoseMod";
        paths.coreDirectory = paths.roseModRoot / L"Core";
        paths.dotnetDirectory = paths.gameRoot / L"dotnet";

        if (std::filesystem::exists(paths.gameRoot / L"GameAssembly.dll")
            || std::filesystem::exists(paths.dataDirectory / L"il2cpp_data"))
        {
            paths.backend = UnityBackend::Il2Cpp;
        }
        else if (std::filesystem::exists(paths.dataDirectory / L"Managed"))
        {
            paths.backend = UnityBackend::Mono;
        }

        return paths;
    }

    std::wstring BackendName(UnityBackend backend)
    {
        switch (backend)
        {
        case UnityBackend::Mono:
            return L"Mono";
        case UnityBackend::Il2Cpp:
            return L"IL2CPP";
        default:
            return L"Unknown";
        }
    }

    std::string ToUtf8(const std::wstring& value)
    {
        if (value.empty())
            return {};

        const auto size = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, nullptr, 0, nullptr, nullptr);
        std::string result(static_cast<size_t>(size - 1), '\0');
        WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, result.data(), size, nullptr, nullptr);
        return result;
    }

    std::wstring FromUtf8(const std::string& value)
    {
        if (value.empty())
            return {};

        const auto size = MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, nullptr, 0);
        std::wstring result(static_cast<size_t>(size - 1), L'\0');
        MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, result.data(), size);
        return result;
    }

    std::wstring JoinForProperty(const std::vector<std::filesystem::path>& paths)
    {
        std::wstringstream joined;
        auto first = true;
        for (const auto& path : paths)
        {
            if (path.empty() || !std::filesystem::exists(path))
                continue;
            if (!first)
                joined << L';';
            joined << path.wstring();
            first = false;
        }
        return joined.str();
    }
}
