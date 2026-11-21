// Run on the interactive desktop during an exclusive display test.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <cstdio>
#include <cwchar>
#include <vector>

int wmain(int argc, wchar_t** argv)
{
    if (argc != 3 || (wcscmp(argv[2], L"read") && wcscmp(argv[2], L"on") && wcscmp(argv[2], L"off"))) {
        std::fputs("Usage: hdr_display_mode.exe <monitor friendly name> <read|on|off>\n", stderr);
        return 1;
    }
    std::vector<DISPLAYCONFIG_PATH_INFO> paths;
    std::vector<DISPLAYCONFIG_MODE_INFO> modes;
    UINT32 pathCount = 0;
    UINT32 modeCount = 0;
    LONG result;
    do {
        result = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, &pathCount, &modeCount);
        if (result != ERROR_SUCCESS) break;
        paths.resize(pathCount);
        modes.resize(modeCount);
        result = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, &pathCount, paths.data(), &modeCount, modes.data(), nullptr);
    } while (result == ERROR_INSUFFICIENT_BUFFER);
    if (result != ERROR_SUCCESS) {
        std::fprintf(stderr, "Active display query failed: %ld\n", result);
        return 1;
    }

    const DISPLAYCONFIG_PATH_TARGET_INFO* selected = nullptr;
    for (UINT32 i = 0; i < pathCount; ++i) {
        const auto& target = paths[i].targetInfo;
        DISPLAYCONFIG_TARGET_DEVICE_NAME name{};
        name.header = {DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME, sizeof(name), target.adapterId, target.id};
        if (DisplayConfigGetDeviceInfo(&name.header) != ERROR_SUCCESS ||
            wcscmp(argv[1], name.monitorFriendlyDeviceName) != 0) continue;
        if (selected) {
            std::fputs("More than one active display matched.\n", stderr);
            return 1;
        }
        selected = &target;
    }
    if (!selected) {
        std::fputs("The named monitor has no active display path.\n", stderr);
        return 1;
    }
    const auto& target = *selected;
    DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 color{};
    color.header = {DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2, sizeof(color), target.adapterId, target.id};
    result = DisplayConfigGetDeviceInfo(&color.header);
    if (result != ERROR_SUCCESS || !color.highDynamicRangeSupported) {
        std::fprintf(stderr, "Target HDR capability unavailable: %ld\n", result);
        return 1;
    }
    if (wcscmp(argv[2], L"read") == 0) {
        std::printf("%s\n", color.highDynamicRangeUserEnabled ? "on" : "off");
        return 0;
    }

    DISPLAYCONFIG_SET_HDR_STATE set{};
    set.header = {DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE, sizeof(set), target.adapterId, target.id};
    set.enableHdr = wcscmp(argv[2], L"on") == 0;
    result = DisplayConfigSetDeviceInfo(&set.header);
    if (result != ERROR_SUCCESS) {
        std::fprintf(stderr, "Setting HDR failed: %ld\n", result);
        return 1;
    }
    return 0;
}
