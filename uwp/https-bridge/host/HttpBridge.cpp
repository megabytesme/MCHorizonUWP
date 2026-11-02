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
    auto input =
        await_with_timeout(response->Content->ReadAsInputStreamAsync());
    std::vector<unsigned char> bytes;
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
extern "C" void __cdecl horizon_uwp_http_free(void *data) { free(data); }
