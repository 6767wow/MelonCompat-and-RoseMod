#include "native_log.h"

#include <chrono>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <windows.h>

namespace rosemod
{
    std::mutex NativeLog::gate_;
    std::filesystem::path NativeLog::logPath_;

    void NativeLog::Initialize(const std::filesystem::path& gameRoot)
    {
        std::lock_guard<std::mutex> lock(gate_);
        logPath_ = gameRoot / L"RoseMod" / L"Logs" / L"RoseMod.native.log";
        std::filesystem::create_directories(logPath_.parent_path());
        std::wofstream(logPath_, std::ios::trunc).close();
    }

    void NativeLog::Banner()
    {
        Info(L"============================================================");
        Info(L" RoseMod Native 0.2.0 - C++ bootstrap/runtime host");
        Info(L"============================================================");
    }

    void NativeLog::Info(const std::wstring& message)
    {
        Write(L"Info", message);
    }

    void NativeLog::Warning(const std::wstring& message)
    {
        Write(L"Warning", message);
    }

    void NativeLog::Error(const std::wstring& message)
    {
        Write(L"Error", message);
    }

    void NativeLog::Write(const wchar_t* level, const std::wstring& message)
    {
        std::lock_guard<std::mutex> lock(gate_);
        SYSTEMTIME now{};
        GetLocalTime(&now);

        std::wstringstream line;
        line << L'['
             << std::setw(2) << std::setfill(L'0') << now.wHour << L':'
             << std::setw(2) << std::setfill(L'0') << now.wMinute << L':'
             << std::setw(2) << std::setfill(L'0') << now.wSecond << L'.'
             << std::setw(3) << std::setfill(L'0') << now.wMilliseconds
             << L"] [" << level << L"] " << message;

        OutputDebugStringW((line.str() + L"\n").c_str());
        if (!logPath_.empty())
        {
            std::wofstream output(logPath_, std::ios::app);
            output << line.str() << L'\n';
        }
    }
}
