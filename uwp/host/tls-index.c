unsigned long _tls_index = 0;
int _fltused = 0x9875;

typedef void (__cdecl *HorizonAtExitCallback)(void);
__declspec(dllimport) int __cdecl _crt_atexit(HorizonAtExitCallback callback);

int __cdecl atexit(HorizonAtExitCallback callback)
{
    return _crt_atexit(callback);
}
