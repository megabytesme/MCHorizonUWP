#include <windows.h>
#include <cstdio>
#include <string>
#include <vector>

#include <EGL/egl.h>
#include <EGL/eglext.h>
#include <EGL/eglext_angle.h>
#include <GLES3/gl3.h>

using namespace Windows::Foundation;
using namespace Windows::Foundation::Collections;
using namespace Windows::Storage;
using namespace Windows::UI::Xaml;
using namespace Windows::UI::Xaml::Controls;

namespace {
class EglProbe {
public:
    explicit EglProbe(FILE* report) : report_(report) {}

    void run(SwapChainPanel^ panel) {
        egl_ = LoadPackagedLibrary(L"libEGL.dll", 0);
        gles_ = LoadPackagedLibrary(L"libGLESv2.dll", 0);
        if (!egl_ || !gles_ || !load_egl() || !load_gl()) {
            std::fprintf(report_, "library_load=failed win32=%lu\n", GetLastError());
            return;
        }
        const auto get_platform_display = reinterpret_cast<PFNEGLGETPLATFORMDISPLAYEXTPROC>(
            get_proc_address_("eglGetPlatformDisplayEXT"));
        if (!get_platform_display) {
            std::fprintf(report_, "eglGetPlatformDisplayEXT=missing\n");
            return;
        }
        const EGLint display_attributes[] = {
            EGL_PLATFORM_ANGLE_TYPE_ANGLE, EGL_PLATFORM_ANGLE_TYPE_D3D11_ANGLE,
            EGL_PLATFORM_ANGLE_DEVICE_TYPE_ANGLE, EGL_PLATFORM_ANGLE_DEVICE_TYPE_HARDWARE_ANGLE,
            EGL_NONE,
        };
        std::fprintf(report_, "egl_client_extensions=%s\n", safe(query_string_(EGL_NO_DISPLAY, EGL_EXTENSIONS)));
        display_ = get_platform_display(EGL_PLATFORM_ANGLE_ANGLE, EGL_DEFAULT_DISPLAY, display_attributes);
        EGLint major = 0, minor = 0;
        if (display_ == EGL_NO_DISPLAY || !initialize_(display_, &major, &minor)) {
            std::fprintf(report_, "egl_initialize=failed error=0x%x\n", get_error_());
            return;
        }
        std::fprintf(report_, "egl_version=%d.%d\negl_vendor=%s\negl_display_extensions=%s\n",
            major, minor, safe(query_string_(display_, EGL_VENDOR)), safe(query_string_(display_, EGL_EXTENSIONS)));

        EGLint count = 0;
        if (!get_configs_(display_, nullptr, 0, &count) || count <= 0) {
            std::fprintf(report_, "egl_configs=failed error=0x%x\n", get_error_());
            return;
        }
        std::vector<EGLConfig> configs(count);
        if (!get_configs_(display_, configs.data(), count, &count)) {
            std::fprintf(report_, "egl_configs=failed error=0x%x\n", get_error_());
            return;
        }

        const auto properties = ref new PropertySet();
        properties->Insert(L"EGLNativeWindowTypeProperty", panel);
        properties->Insert(L"EGLRenderSurfaceSizeProperty", PropertyValue::CreateSize(Size(320, 240)));
        const auto window = reinterpret_cast<EGLNativeWindowType>(reinterpret_cast<IInspectable*>(properties));

        const EGLConfig sdr = find_config(configs, 8, 8, EGL_COLOR_COMPONENT_TYPE_FIXED_EXT);
        const EGLConfig rgb10 = find_config(configs, 10, 2, EGL_COLOR_COMPONENT_TYPE_FIXED_EXT);
        const EGLConfig fp16 = find_config(configs, 16, 16, EGL_COLOR_COMPONENT_TYPE_FLOAT_EXT);
        attempt("rgba8-default", sdr, window, EGL_NONE, true);
        attempt("rgba8-srgb", sdr, window, EGL_GL_COLORSPACE_SRGB, false);
        attempt("rgba8-display-p3", sdr, window, EGL_GL_COLORSPACE_DISPLAY_P3_EXT, false);
        attempt("rgb10-default", rgb10, window, EGL_NONE, false);
        attempt("rgb10-pq", rgb10, window, EGL_GL_COLORSPACE_BT2020_PQ_EXT, false);
        attempt("fp16-default", fp16, window, EGL_NONE, false);
        attempt("fp16-scrgb-linear", fp16, window, EGL_GL_COLORSPACE_SCRGB_LINEAR_EXT, false);
        attempt("fp16-bt2020-linear", fp16, window, EGL_GL_COLORSPACE_BT2020_LINEAR_EXT, false);
        std::fprintf(report_, "egl_terminate=%u\n", terminate_(display_));
    }

private:
    static const char* safe(const char* value) { return value ? value : "(unavailable)"; }

    bool load_egl() {
        #define EGL_FUNCTION(type, name, function) \
            name##_ = reinterpret_cast<type>(GetProcAddress(egl_, #function)); if (!name##_) return false;
        #include "angle-panel-probe-egl.inc"
        #undef EGL_FUNCTION
        return true;
    }

    bool load_gl() {
        gl_string_ = reinterpret_cast<PFNGLGETSTRINGPROC>(GetProcAddress(gles_, "glGetString"));
        gl_integer_ = reinterpret_cast<PFNGLGETINTEGERVPROC>(GetProcAddress(gles_, "glGetIntegerv"));
        gl_get_attachment_ = reinterpret_cast<PFNGLGETFRAMEBUFFERATTACHMENTPARAMETERIVPROC>(
            GetProcAddress(gles_, "glGetFramebufferAttachmentParameteriv"));
        gl_get_error_ = reinterpret_cast<PFNGLGETERRORPROC>(GetProcAddress(gles_, "glGetError"));
        return gl_string_ && gl_integer_ && gl_get_attachment_ && gl_get_error_;
    }

    EGLint config_attribute(EGLConfig config, EGLint attribute) const {
        EGLint value = -1;
        get_config_attribute_(display_, config, attribute, &value);
        return value;
    }

    EGLConfig find_config(const std::vector<EGLConfig>& configs, EGLint rgb, EGLint alpha, EGLint type) const {
        for (const auto config : configs) {
            if ((config_attribute(config, EGL_SURFACE_TYPE) & EGL_WINDOW_BIT) &&
                (config_attribute(config, EGL_RENDERABLE_TYPE) & EGL_OPENGL_ES3_BIT_KHR) &&
                config_attribute(config, EGL_RED_SIZE) == rgb &&
                config_attribute(config, EGL_GREEN_SIZE) == rgb &&
                config_attribute(config, EGL_BLUE_SIZE) == rgb &&
                config_attribute(config, EGL_ALPHA_SIZE) == alpha &&
                config_attribute(config, EGL_COLOR_COMPONENT_TYPE_EXT) == type &&
                config_attribute(config, EGL_SAMPLE_BUFFERS) == 0) {
                return config;
            }
        }
        return nullptr;
    }

    void attempt(const char* label, EGLConfig config, EGLNativeWindowType window, EGLint colorspace, bool log_gl) {
        std::fprintf(report_, "\n[%s]\n", label);
        if (!config) {
            std::fprintf(report_, "config=unavailable\n");
            return;
        }
        std::fprintf(report_, "config_id=%d config_rgba=%d/%d/%d/%d component_type=0x%x\n",
            config_attribute(config, EGL_CONFIG_ID), config_attribute(config, EGL_RED_SIZE),
            config_attribute(config, EGL_GREEN_SIZE), config_attribute(config, EGL_BLUE_SIZE),
            config_attribute(config, EGL_ALPHA_SIZE), config_attribute(config, EGL_COLOR_COMPONENT_TYPE_EXT));
        const EGLint surface_attrs[] = { EGL_GL_COLORSPACE, colorspace, EGL_NONE };
        const auto surface = create_surface_(display_, config, window, colorspace == EGL_NONE ? nullptr : surface_attrs);
        if (surface == EGL_NO_SURFACE) {
            std::fprintf(report_, "surface=failed error=0x%x\n", get_error_());
            return;
        }
        const EGLint context_attrs[] = { EGL_CONTEXT_CLIENT_VERSION, 3, EGL_NONE };
        const auto context = create_context_(display_, config, EGL_NO_CONTEXT, context_attrs);
        if (context == EGL_NO_CONTEXT) {
            std::fprintf(report_, "context=failed error=0x%x\n", get_error_());
        } else if (!make_current_(display_, surface, surface, context)) {
            std::fprintf(report_, "make_current=failed error=0x%x\n", get_error_());
        } else {
            EGLint actual_colorspace = 0;
            if (query_surface_(display_, surface, EGL_GL_COLORSPACE, &actual_colorspace)) {
                std::fprintf(report_, "egl_surface_colorspace=0x%x\n", actual_colorspace);
            } else {
                std::fprintf(report_, "egl_surface_colorspace=unavailable error=0x%x\n", get_error_());
            }
            report_framebuffer(log_gl);
            if (swap_buffers_(display_, surface)) {
                std::fprintf(report_, "swap_buffers=success\n");
            } else {
                std::fprintf(report_, "swap_buffers=failed error=0x%x\n", get_error_());
            }
            make_current_(display_, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);
        }
        if (context != EGL_NO_CONTEXT) destroy_context_(display_, context);
        destroy_surface_(display_, surface);
    }

    void report_framebuffer(bool log_gl) const {
        if (log_gl) {
            std::fprintf(report_, "gl_version=%s\ngl_renderer=%s\ngl_extensions=%s\n",
                safe(reinterpret_cast<const char*>(gl_string_(GL_VERSION))),
                safe(reinterpret_cast<const char*>(gl_string_(GL_RENDERER))),
                safe(reinterpret_cast<const char*>(gl_string_(GL_EXTENSIONS))));
        }
        GLint rgba[4] = {};
        gl_integer_(GL_RED_BITS, &rgba[0]);
        gl_integer_(GL_GREEN_BITS, &rgba[1]);
        gl_integer_(GL_BLUE_BITS, &rgba[2]);
        gl_integer_(GL_ALPHA_BITS, &rgba[3]);
        GLint encoding = 0, type = 0;
        gl_get_attachment_(GL_FRAMEBUFFER, GL_BACK, GL_FRAMEBUFFER_ATTACHMENT_COLOR_ENCODING, &encoding);
        gl_get_attachment_(GL_FRAMEBUFFER, GL_BACK, GL_FRAMEBUFFER_ATTACHMENT_COMPONENT_TYPE, &type);
        std::fprintf(report_, "gl_default_rgba=%d/%d/%d/%d color_encoding=0x%x component_type=0x%x error=0x%x\n",
            rgba[0], rgba[1], rgba[2], rgba[3], encoding, type, gl_get_error_());
    }

    FILE* report_;
    HMODULE egl_ = nullptr;
    HMODULE gles_ = nullptr;
    EGLDisplay display_ = EGL_NO_DISPLAY;
    #define EGL_FUNCTION(type, name, function) type name##_ = nullptr;
    #include "angle-panel-probe-egl.inc"
    #undef EGL_FUNCTION
    PFNGLGETSTRINGPROC gl_string_ = nullptr;
    PFNGLGETINTEGERVPROC gl_integer_ = nullptr;
    PFNGLGETFRAMEBUFFERATTACHMENTPARAMETERIVPROC gl_get_attachment_ = nullptr;
    PFNGLGETERRORPROC gl_get_error_ = nullptr;
};

ref class App sealed : Application {
    bool ran_ = false;

protected:
    void OnLaunched(Windows::ApplicationModel::Activation::LaunchActivatedEventArgs^) override {
        const auto panel = ref new SwapChainPanel();
        panel->Loaded += ref new RoutedEventHandler(this, &App::on_panel_loaded);
        Window::Current->Content = panel;
        Window::Current->Activate();
    }

private:
    void on_panel_loaded(Platform::Object^ sender, RoutedEventArgs^) {
        if (ran_) return;
        ran_ = true;
        const std::wstring path = std::wstring(ApplicationData::Current->LocalFolder->Path->Data()) +
            L"\\angle-panel-probe.txt";
        FILE* report = nullptr;
        if (_wfopen_s(&report, path.c_str(), L"wb") == 0) {
            std::fprintf(report, "ANGLE XAML SwapChainPanel probe\n");
            EglProbe probe(report);
            probe.run(safe_cast<SwapChainPanel^>(sender));
            std::fprintf(report, "probe_complete=true\n");
            std::fclose(report);
        }
        Application::Current->Exit();
    }
};
}

[Platform::MTAThread]
int main(Platform::Array<Platform::String^>^) {
    Application::Start(ref new ApplicationInitializationCallback([](ApplicationInitializationCallbackParams^) {
        ref new App();
    }));
    return 0;
}
