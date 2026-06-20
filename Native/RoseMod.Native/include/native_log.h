#pragma once

#include <filesystem>
#include <mutex>
#include <string>

namespace rosemod
{
    class NativeLog
    {
    public:
        static void Initialize(const std::filesystem::path& gameRoot);
        static void Banner();
        static void Info(const std::wstring& message);
        static void Warning(const std::wstring& message);
        static void Error(const std::wstring& message);

    private:
        static void Write(const wchar_t* level, const std::wstring& message);
        static std::mutex gate_;
        static std::filesystem::path logPath_;
    };
}
