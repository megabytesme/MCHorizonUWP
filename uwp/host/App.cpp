#include <windows.h>
#include "Dx11Renderer.h"

#ifdef HORIZON_UWP_CORE_BRIDGE
extern "C" int horizon_uwp_protocol_version();
#endif

using namespace Windows::ApplicationModel::Activation;
using namespace Windows::ApplicationModel::Core;
using namespace Windows::Foundation;
using namespace Windows::UI::Core;

ref class AppView sealed : IFrameworkView {
internal:
    AppView() : renderer_(new horizon_uwp::Dx11Renderer()), closed_(false), visible_(true) {}
public:
    virtual ~AppView() { delete renderer_; }
public:
    virtual void Initialize(CoreApplicationView^ view) {
#ifdef HORIZON_UWP_CORE_BRIDGE
        if (horizon_uwp_protocol_version() <= 0) throw ref new Platform::FailureException();
#endif
        view->Activated += ref new TypedEventHandler<CoreApplicationView^, IActivatedEventArgs^>(this, &AppView::Activated);
    }
    virtual void SetWindow(CoreWindow^ window) {
        window_ = window;
        window->Closed += ref new TypedEventHandler<CoreWindow^, CoreWindowEventArgs^>(this, &AppView::Closed);
        window->VisibilityChanged += ref new TypedEventHandler<CoreWindow^, VisibilityChangedEventArgs^>(this, &AppView::VisibilityChanged);
        window->SizeChanged += ref new TypedEventHandler<CoreWindow^, WindowSizeChangedEventArgs^>(this, &AppView::SizeChanged);
        Check(renderer_->Initialize(reinterpret_cast<IUnknown*>(window)));
    }
    virtual void Load(Platform::String^) {}
    virtual void Run() {
        while (!closed_) {
            if (visible_) {
                window_->Dispatcher->ProcessEvents(CoreProcessEventsOption::ProcessAllIfPresent);
                if (!closed_) Check(renderer_->Render());
            } else {
                window_->Dispatcher->ProcessEvents(CoreProcessEventsOption::ProcessOneAndAllPending);
            }
        }
    }
    virtual void Uninitialize() {}
private:
    void Check(HRESULT hr) { if (FAILED(hr)) throw Platform::Exception::CreateException(hr); }
    void Activated(CoreApplicationView^, IActivatedEventArgs^) { window_->Activate(); }
    void Closed(CoreWindow^, CoreWindowEventArgs^) { closed_ = true; }
    void VisibilityChanged(CoreWindow^, VisibilityChangedEventArgs^ args) { visible_ = args->Visible; }
    void SizeChanged(CoreWindow^, WindowSizeChangedEventArgs^) { Check(renderer_->Resize()); }
    CoreWindow^ window_;
    horizon_uwp::Dx11Renderer* renderer_;
    bool closed_;
    bool visible_;
};

ref class AppViewSource sealed : IFrameworkViewSource {
public:
    virtual IFrameworkView^ CreateView() { return ref new AppView(); }
};

[Platform::MTAThread]
int main(Platform::Array<Platform::String^>^) {
    CoreApplication::Run(ref new AppViewSource());
    return 0;
}

