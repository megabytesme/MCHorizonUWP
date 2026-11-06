// Newer MSVC STL headers import these C names even for AppContainer builds,
// while the UWP SDK hides their declarations. They are declared only to let
// <cstdlib> compile; this bridge must never call either desktop-only API.
#if defined(_MSC_VER) && _MSC_VER >= 1940 && defined(WINAPI_FAMILY)
extern "C" __declspec(dllimport) char* __cdecl getenv(const char*);
extern "C" __declspec(dllimport) int __cdecl system(const char*);
#endif

#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <ppltasks.h>
#include <roapi.h>
#include <string>
#include <vector>
#include <windows.h>
#include <windows.storage.streams.h>
#include <windows.system.threading.h>
#include <windows.web.http.filters.h>
#include <windows.web.http.h>

using namespace Concurrency;
using namespace Windows::Foundation;
using namespace Windows::Storage::Streams;
using namespace Windows::System::Threading;
using namespace Windows::Web::Http;
using namespace Windows::Web::Http::Filters;

namespace {
// Each network operation has a 15 second deadline; Cancel is safe after
// completion.
template <class T, class P>
T await_with_timeout(IAsyncOperationWithProgress<T, P> ^ op) {
  TimeSpan timeout;
  timeout.Duration = 15LL * 10000000;
  auto timer = ThreadPoolTimer::CreateTimer(
      ref new TimerElapsedHandler([op](ThreadPoolTimer ^) { op->Cancel(); }),
      timeout);
  try {
    auto value = create_task(op).get();
    timer->Cancel();
    return value;
  } catch (...) {
    timer->Cancel();
    throw;
  }
}
template <class T> T await_with_timeout(IAsyncOperation<T> ^ op) {
  TimeSpan timeout;
  timeout.Duration = 15LL * 10000000;
  auto timer = ThreadPoolTimer::CreateTimer(
      ref new TimerElapsedHandler([op](ThreadPoolTimer ^) { op->Cancel(); }),
      timeout);
  try {
    auto value = create_task(op).get();
    timer->Cancel();
    return value;
  } catch (...) {
    timer->Cancel();
    throw;
  }
}
std::vector<unsigned char> read_body(HttpResponseMessage ^ response,
                                     size_t limit) {
  std::vector<unsigned char> bytes;
  if (!response->Content)
    return bytes;
  auto input = await_with_timeout(response->Content->ReadAsInputStreamAsync());
  for (;;) {
    auto chunk = await_with_timeout(input->ReadAsync(
        ref new Buffer(65536), 65536, InputStreamOptions::Partial));
    unsigned int count = chunk->Length;
    if (!count)
      break;
    if (count > limit - bytes.size())
      throw ref new Platform::FailureException();
    auto array = ref new Platform::Array<unsigned char>(count);
    DataReader::FromBuffer(chunk)->ReadBytes(array);
    bytes.insert(bytes.end(), array->begin(), array->end());
  }
  return bytes;
}
} // namespace
extern "C" int __cdecl horizon_uwp_http_get(const unsigned char *url,
                                            size_t url_len, size_t limit,
                                            unsigned char **output,
                                            size_t *output_len) {
  if (!url || !output || !output_len || !url_len || url_len > 8192 ||
      limit > 16 * 1024 * 1024)
    return -1;
  *output = nullptr;
  *output_len = 0;
  HRESULT init = RoInitialize(RO_INIT_MULTITHREADED);
  if (FAILED(init))
    return -1; // Do not block a UI apartment.
  int result = -1;
  try {
    int n = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
                                reinterpret_cast<const char *>(url),
                                static_cast<int>(url_len), nullptr, 0);
    if (n <= 0)
      throw ref new Platform::InvalidArgumentException();
    std::wstring wide(n, L'\0');
    if (!MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
                             reinterpret_cast<const char *>(url),
                             static_cast<int>(url_len), &wide[0], n))
      throw ref new Platform::InvalidArgumentException();
    auto uri = ref new Uri(ref new Platform::String(wide.data(), n));
    if (uri->SchemeName != L"https" && uri->SchemeName != L"http")
      throw ref new Platform::InvalidArgumentException();
    const bool secure = uri->SchemeName == L"https";
    auto filter = ref new HttpBaseProtocolFilter();
    filter->AllowAutoRedirect = false;
    auto client = ref new HttpClient(filter);
    client->DefaultRequestHeaders->UserAgent->TryParseAdd(L"Java");
    HttpResponseMessage ^ response = nullptr;
    for (int redirects = 0; redirects <= 5; ++redirects) {
      response = await_with_timeout(
          client->GetAsync(uri, HttpCompletionOption::ResponseHeadersRead));
      int status = static_cast<int>(response->StatusCode);
      if (status < 300 || status >= 400)
        break;
      if (redirects == 5 || !response->Headers->Location)
        throw ref new Platform::FailureException();
      uri = ref new Uri(uri->AbsoluteUri, response->Headers->Location->RawUri);
      if ((uri->SchemeName != L"https" && uri->SchemeName != L"http") ||
          (secure && uri->SchemeName != L"https"))
        throw ref new Platform::FailureException();
    }
    if (!response || !response->IsSuccessStatusCode)
      throw ref new Platform::FailureException();
    auto bytes = read_body(response, limit);
    auto data =
        static_cast<unsigned char *>(malloc(std::max<size_t>(1, bytes.size())));
    if (!data)
      throw ref new Platform::OutOfMemoryException();
    if (!bytes.empty())
      memcpy(data, bytes.data(), bytes.size());
    *output = data;
    *output_len = bytes.size();
    result = 0;
  } catch (...) {
  }
  RoUninitialize();
  return result;
}

// Authenticated requests never follow redirects: a bearer token and POST body
// must not be forwarded to a different endpoint implicitly.
static int http_request_impl(
    const unsigned char *url, size_t url_len, int method,
    const unsigned char *authorization, size_t authorization_len,
    const unsigned char *content_type, size_t content_type_len,
    const unsigned char *body, size_t body_len, size_t limit,
    unsigned int *http_status, unsigned char **output, size_t *output_len,
    bool zstd) {
  if (!url || !output || !output_len || !http_status || !url_len ||
      url_len > 8192 || authorization_len > 16384 || content_type_len > 256 ||
      body_len > 1024 * 1024 || limit > 16 * 1024 * 1024 ||
      (authorization_len && !authorization) ||
      (content_type_len && !content_type) || (body_len && !body) ||
      (method < 0 || method > 2))
    return -1;
  *output = nullptr;
  *output_len = 0;
  *http_status = 0;
  HRESULT init = RoInitialize(RO_INIT_MULTITHREADED);
  if (FAILED(init))
    return -1;
  int result = -1;
  try {
    auto utf8 = [](const unsigned char *data,
                   size_t length) -> Platform::String ^ {
      if (!length)
        return ref new Platform::String(L"");
      std::string text(reinterpret_cast<const char *>(data), length);
      if (text.find('\0') != std::string::npos)
        throw ref new Platform::InvalidArgumentException();
      int n = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(),
                                  static_cast<int>(length), nullptr, 0);
      if (n <= 0)
        throw ref new Platform::InvalidArgumentException();
      std::wstring wide(n, L'\0');
      if (!MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(),
                               static_cast<int>(length), &wide[0], n))
        throw ref new Platform::InvalidArgumentException();
      return ref new Platform::String(wide.data(), n);
    };
    auto header = [&](const unsigned char *data,
                      size_t length) -> Platform::String ^ {
      if (length && (memchr(data, '\r', length) || memchr(data, '\n', length)))
        throw ref new Platform::InvalidArgumentException();
      return utf8(data, length);
    };
    auto uri = ref new Uri(utf8(url, url_len));
    if (uri->SchemeName != L"https")
      throw ref new Platform::InvalidArgumentException();
    auto filter = ref new HttpBaseProtocolFilter();
    filter->AllowAutoRedirect = false;
    auto client = ref new HttpClient(filter);
    client->DefaultRequestHeaders->UserAgent->TryParseAdd(L"Horizon");
    auto request = ref new HttpRequestMessage(
        method == 1 ? HttpMethod::Post : method == 2 ? HttpMethod::Delete : HttpMethod::Get, uri);
    if (authorization_len &&
        !request->Headers->TryAppendWithoutValidation(
            L"Authorization", header(authorization, authorization_len)))
      throw ref new Platform::InvalidArgumentException();
    if (method == 1) {
      auto writer = ref new DataWriter();
      if (body_len) {
        auto array = ref new Platform::Array<unsigned char>(
            static_cast<unsigned int>(body_len));
        memcpy(array->Data, body, body_len);
        writer->WriteBytes(array);
      }
      request->Content = ref new HttpBufferContent(writer->DetachBuffer());
      if (content_type_len &&
          !request->Content->Headers->TryAppendWithoutValidation(
              L"Content-Type", header(content_type, content_type_len)))
        throw ref new Platform::InvalidArgumentException();
      if (zstd && !request->Content->Headers->TryAppendWithoutValidation(L"Content-Encoding", L"zstd"))
        throw ref new Platform::InvalidArgumentException();
    } else if (body_len || content_type_len)
      throw ref new Platform::InvalidArgumentException();
    auto response = await_with_timeout(client->SendRequestAsync(
        request, HttpCompletionOption::ResponseHeadersRead));
    *http_status = static_cast<unsigned int>(response->StatusCode);
    auto bytes = read_body(response, limit);
    auto data =
        static_cast<unsigned char *>(malloc(std::max<size_t>(1, bytes.size())));
    if (!data)
      throw ref new Platform::OutOfMemoryException();
    if (!bytes.empty())
      memcpy(data, bytes.data(), bytes.size());
    *output = data;
    *output_len = bytes.size();
    result = 0;
  } catch (...) {
  }
  RoUninitialize();
  return result;
}

extern "C" int __cdecl horizon_uwp_http_request(
    const unsigned char *url, size_t url_len, int method,
    const unsigned char *authorization, size_t authorization_len,
    const unsigned char *content_type, size_t content_type_len,
    const unsigned char *body, size_t body_len, size_t limit,
    unsigned int *http_status, unsigned char **output, size_t *output_len) {
  return http_request_impl(url, url_len, method, authorization, authorization_len,
      content_type, content_type_len, body, body_len, limit, http_status, output, output_len, false);
}

extern "C" int __cdecl horizon_uwp_http_post_zstd(
    const unsigned char *url, size_t url_len, const unsigned char *body, size_t body_len,
    size_t limit, unsigned int *http_status, unsigned char **output, size_t *output_len) {
  static const unsigned char json[] = "application/json";
  return http_request_impl(url, url_len, 1, nullptr, 0, json, sizeof(json) - 1,
      body, body_len, limit, http_status, output, output_len, true);
}

extern "C" void __cdecl horizon_uwp_http_free(void *data) { free(data); }
