#pragma once

#include <filesystem>
#include <string>
#include <vector>

namespace rosemod
{
    enum class UnityBackend
    {
        Unknown,
        Mono,
        Il2Cpp
    };

    struct RuntimePaths
    {
        std::filesystem::path modulePath;
        std::filesystem::path gameRoot;
        std::filesystem::path gameExe;
        std::filesystem::path dataDirectory;
        std::filesystem::path roseModRoot;
        std::filesystem::path coreDirectory;
        std::filesystem::path dotnetDirectory;
        UnityBackend backend = UnityBackend::Unknown;
    };

    RuntimePaths DetectRuntimePaths(void* moduleHandle);
    std::wstring BackendName(UnityBackend backend);
    std::string ToUtf8(const std::wstring& value);
    std::wstring FromUtf8(const std::string& value);
    std::wstring JoinForProperty(const std::vector<std::filesystem::path>& paths);
}
