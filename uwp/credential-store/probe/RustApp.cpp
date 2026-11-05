#include <ppltasks.h>
#include <string>
#include <windows.h>
#include <windows.security.credentials.h>
#include <windows.ui.popups.h>
using namespace Concurrency;
extern "C" int __cdecl horizon_vault_probe();
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
    int result = horizon_vault_probe();
    result_ = result == 0
                  ? L"PASS: Rust vault adapter round trip and cleanup"
                  : L"FAIL: Rust vault adapter step " + std::to_wstring(result);
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
