#include <ppltasks.h>
#include <string>
#include <windows.h>
#include <windows.security.credentials.h>
#include <windows.ui.popups.h>
using namespace Concurrency;
extern "C" int __cdecl horizon_vault_set(const unsigned char *, size_t,
                                         const unsigned char *, size_t);
extern "C" int __cdecl horizon_vault_get(const unsigned char *, size_t,
                                         unsigned char **, size_t *);
extern "C" int __cdecl horizon_vault_remove(const unsigned char *, size_t);
extern "C" void __cdecl horizon_vault_free(void *, size_t);
using namespace Windows::ApplicationModel::Core;
using namespace Windows::ApplicationModel::Activation;
using namespace Windows::Foundation;
using namespace Windows::UI::Core;
using namespace Windows::Web::Http;
using namespace Windows::Storage;
ref class HttpsProbeView sealed : IFrameworkView {
  CoreWindow ^ window_;
  bool closed_ = false;
  bool started_ = false;
  HttpClient ^ client_;
  std::wstring result_;
  void save_and_display() {
    std::wstring path = ApplicationData::Current->LocalFolder->Path->Data();
    path += L"\\vault-probe.txt";
    CREATEFILE2_EXTENDED_PARAMETERS args{};
    args.dwSize = sizeof(args);
    HANDLE h = CreateFile2(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ,
                           CREATE_ALWAYS, &args);
    if (h != INVALID_HANDLE_VALUE) {
      DWORD written;
      WriteFile(h, result_.data(),
                static_cast<DWORD>(result_.size() * sizeof(wchar_t)), &written,
                nullptr);
      CloseHandle(h);
    }
    (ref new Windows::UI::Popups::MessageDialog(
         ref new Platform::String(result_.c_str()),
         L"Horizon credential probe"))
        ->ShowAsync();
  }
  void activated(CoreApplicationView ^, IActivatedEventArgs ^) {
    window_->Activate();
    if (started_)
      return;
    started_ = true;
    client_ = ref new HttpClient();
    std::string key = "synthetic-probe-" +
                      std::to_string(GetCurrentProcessId()) + "-" +
                      std::to_string(GetTickCount64());
    std::string one(8192, 'X'), two("replacement-test-value");
    auto p = reinterpret_cast<const unsigned char *>(key.data());
    unsigned char *out = nullptr;
    size_t n = 0;
    int missing = horizon_vault_get(p, key.size(), &out, &n);
    int put = horizon_vault_set(
        p, key.size(), reinterpret_cast<const unsigned char *>(one.data()),
        one.size());
    int get = horizon_vault_get(p, key.size(), &out, &n);
    bool first = get == 0 && n == one.size() && !memcmp(out, one.data(), n);
    if (out)
      horizon_vault_free(out, n);
    out = nullptr;
    n = 0;
    int update = horizon_vault_set(
        p, key.size(), reinterpret_cast<const unsigned char *>(two.data()),
        two.size());
    int get2 = horizon_vault_get(p, key.size(), &out, &n);
    bool second = get2 == 0 && n == two.size() && !memcmp(out, two.data(), n);
    if (out)
      horizon_vault_free(out, n);
    out = nullptr;
    n = 0;
    int removed = horizon_vault_remove(p, key.size());
    int missing2 = horizon_vault_get(p, key.size(), &out, &n);
    if (out)
      horizon_vault_free(out, n);
    result_ = L"Missing: " + std::to_wstring(missing) + L", set: " +
              std::to_wstring(put) + L", long read: " + std::to_wstring(first) +
              L", update: " + std::to_wstring(update) + L", updated read: " +
              std::to_wstring(second) + L", remove: " +
              std::to_wstring(removed) + L", absent: " +
              std::to_wstring(missing2);
    save_and_display();
  }
  void closed(CoreWindow ^, CoreWindowEventArgs ^) { closed_ = true; }

public:
  virtual void Initialize(CoreApplicationView ^ view) {
    view->Activated +=
        ref new TypedEventHandler<CoreApplicationView ^, IActivatedEventArgs ^>(
            this, &HttpsProbeView::activated);
  }
  virtual void SetWindow(CoreWindow ^ window) {
    window_ = window;
    window->Closed +=
        ref new TypedEventHandler<CoreWindow ^, CoreWindowEventArgs ^>(
            this, &HttpsProbeView::closed);
  }
  virtual void Load(Platform::String ^) {}
  virtual void Run() {
    while (!closed_)
      window_->Dispatcher->ProcessEvents(
          CoreProcessEventsOption::ProcessOneAndAllPending);
  }
  virtual void Uninitialize() {}
};
ref class HttpsSource sealed : IFrameworkViewSource {
public:
  virtual IFrameworkView ^ CreateView() { return ref new HttpsProbeView(); }
};
[Platform::MTAThread] int main(Platform::Array<Platform::String ^> ^) {
  CoreApplication::Run(ref new HttpsSource());
  return 0;
}
