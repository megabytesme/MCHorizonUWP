// New MSVC STL headers name these desktop functions even under AppContainer.
// Declarations allow the headers to compile; this bridge never calls them.
#if defined(_MSC_VER) && _MSC_VER >= 1940 && defined(WINAPI_FAMILY)
extern "C" __declspec(dllimport) char* __cdecl getenv(const char*);
extern "C" __declspec(dllimport) int __cdecl system(const char*);
#endif

#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <roapi.h>
#include <string>
#include <windows.h>
#include <windows.security.credentials.h>
using namespace Windows::Security::Credentials;
namespace {
constexpr HRESULT kNotFound = HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
Platform::String ^
    to_string(const unsigned char *bytes, size_t length, size_t limit) {
  if (!bytes || !length || length > limit || memchr(bytes, 0, length))
    throw ref new Platform::InvalidArgumentException();
  int n = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
                              reinterpret_cast<const char *>(bytes),
                              static_cast<int>(length), nullptr, 0);
  if (n <= 0)
    throw ref new Platform::InvalidArgumentException();
  std::wstring value(n, L'\0');
  if (!MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
                           reinterpret_cast<const char *>(bytes),
                           static_cast<int>(length), &value[0], n))
    throw ref new Platform::InvalidArgumentException();
  auto result = ref new Platform::String(value.data(), n);
  SecureZeroMemory(&value[0], value.size() * sizeof(wchar_t));
  return result;
}
Platform::String ^ resource() {
  return ref new Platform::String(L"dev.playhorizon.uwp.accounts");
}
} // namespace
extern "C" int __cdecl horizon_vault_set(const unsigned char *key,
                                         size_t key_len,
                                         const unsigned char *secret,
                                         size_t secret_len) {
  HRESULT init = RoInitialize(RO_INIT_MULTITHREADED);
  if (FAILED(init) && init != RPC_E_CHANGED_MODE)
    return -1;
  int status = -1;
  try {
    auto vault = ref new PasswordVault();
    vault->Add(
        ref new PasswordCredential(resource(), to_string(key, key_len, 256),
                                   to_string(secret, secret_len, 65536)));
    status = 0;
  } catch (...) {
  }
  if (SUCCEEDED(init))
    RoUninitialize();
  return status;
}
extern "C" int __cdecl horizon_vault_get(const unsigned char *key,
                                         size_t key_len, unsigned char **output,
                                         size_t *output_len) {
  if (!output || !output_len)
    return -1;
  *output = nullptr;
  *output_len = 0;
  HRESULT init = RoInitialize(RO_INIT_MULTITHREADED);
  if (FAILED(init) && init != RPC_E_CHANGED_MODE)
    return -1;
  int status = -1;
  try {
    auto vault = ref new PasswordVault();
    auto credential = vault->Retrieve(resource(), to_string(key, key_len, 256));
    credential->RetrievePassword();
    auto value = credential->Password;
    int n = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value->Data(),
                                value->Length(), nullptr, 0, nullptr, nullptr);
    if (n <= 0 || n > 65536)
      throw ref new Platform::FailureException();
    auto data = static_cast<unsigned char *>(malloc(n));
    if (!data)
      throw ref new Platform::OutOfMemoryException();
    if (!WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value->Data(),
                             value->Length(), reinterpret_cast<char *>(data), n,
                             nullptr, nullptr)) {
      SecureZeroMemory(data, n);
      free(data);
      throw ref new Platform::FailureException();
    }
    *output = data;
    *output_len = n;
    status = 0;
  } catch (Platform::Exception ^ ex) {
    if (ex->HResult == kNotFound)
      status = 1;
  } catch (...) {
  }
  if (SUCCEEDED(init))
    RoUninitialize();
  return status;
}
extern "C" int __cdecl horizon_vault_remove(const unsigned char *key,
                                            size_t key_len) {
  HRESULT init = RoInitialize(RO_INIT_MULTITHREADED);
  if (FAILED(init) && init != RPC_E_CHANGED_MODE)
    return -1;
  int status = -1;
  try {
    auto vault = ref new PasswordVault();
    auto credential = vault->Retrieve(resource(), to_string(key, key_len, 256));
    vault->Remove(credential);
    status = 0;
  } catch (Platform::Exception ^ ex) {
    if (ex->HResult == kNotFound)
      status = 1;
  } catch (...) {
  }
  if (SUCCEEDED(init))
    RoUninitialize();
  return status;
}
extern "C" void __cdecl horizon_vault_free(void *data, size_t length) {
  if (data) {
    SecureZeroMemory(data, length);
    free(data);
  }
}
