#include <ppltasks.h>
#include <string>
#include <windows.h>
#include <windows.ui.popups.h>
using namespace Concurrency;
extern "C" int __cdecl horizon_uwp_http_get(const unsigned char *, size_t,
                                            size_t, unsigned char **, size_t *);
extern "C" void __cdecl horizon_uwp_http_free(void *);
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
    path += L"\\https-probe.txt";
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
         ref new Platform::String(result_.c_str()), L"Horizon HTTPS probe"))
        ->ShowAsync();
  }
  void activated(CoreApplicationView ^, IActivatedEventArgs ^) {
    window_->Activate();
    if (started_)
      return;
    started_ = true;
    client_ = ref new HttpClient();
    auto context = task_continuation_context::use_current();
    create_task([]() -> std::wstring {
      auto request = [](const char *url, size_t limit, size_t *n) {
        unsigned char *body = nullptr;
        *n = 0;
        int status =
            horizon_uwp_http_get(reinterpret_cast<const unsigned char *>(url),
                                 strlen(url), limit, &body, n);
        if (body)
          horizon_uwp_http_free(body);
        return status;
      };
      size_t n = 0;
      int good = request(
          "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
          1048576, &n);
      std::wstring text = L"Mojang request result: " + std::to_wstring(good) +
                          L", bytes: " + std::to_wstring(n) + L"\n";
      int bad = request("https://expired.badssl.com/", 1048576, &n);
      text += L"Expired certificate result: " + std::to_wstring(bad) + L"\n";
      int bounded = request(
          "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", 10,
          &n);
      text += L"10-byte limit result: " + std::to_wstring(bounded);
      return text;
    })
        .then(
            [this](std::wstring result) {
              result_ = result;
              save_and_display();
            },
            context);
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
