#include <windows.h>

extern "C" __declspec(dllexport) unsigned horizon_probe_value() {
    return 0x294A64u;
}
