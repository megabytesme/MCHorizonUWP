// Native UWP XAML shell. The game stays in its own DirectX CoreWindow.
#if defined(_MSC_VER) && _MSC_VER >= 1940 && defined(WINAPI_FAMILY)
extern "C" __declspec(dllimport) char* __cdecl getenv(const char*);
extern "C" __declspec(dllimport) int __cdecl system(const char*);
#endif
#using <Microsoft.UI.Xaml.winmd>
#include <windows.h>
#include <d3d11_1.h>
#include <dxgi1_2.h>
#include <ppltasks.h>
#include <windows.applicationmodel.h>
#include <windows.applicationmodel.activation.h>
#include <windows.applicationmodel.core.h>
#include <windows.data.json.h>
#include <windows.foundation.metadata.h>
#include <windows.graphics.display.h>
#include <windows.graphics.imaging.h>
#include <windows.storage.h>
#include <windows.storage.streams.h>
#include <windows.system.profile.h>
#include <windows.system.threading.h>
#include <windows.security.credentials.h>
#include <windows.ui.h>
#include <windows.ui.core.h>
#include <windows.ui.text.h>
#include <windows.ui.viewmanagement.h>
#include <windows.ui.xaml.h>
#include <windows.ui.xaml.controls.h>
#include <windows.ui.xaml.markup.h>
#include <windows.ui.xaml.media.h>
#include <windows.ui.xaml.media.imaging.h>
#include <windows.web.http.h>
#include <windows.web.http.filters.h>
#include <robuffer.h>
#include <wrl/client.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

using namespace Windows::ApplicationModel::Activation;
using namespace Windows::ApplicationModel::Core;
using namespace Windows::Foundation;
using namespace Windows::Graphics::Display;
using namespace Windows::Graphics::Imaging;

using namespace Windows::Storage;
using namespace Windows::Storage::Streams;
using namespace Windows::Data::Json;
using namespace Windows::System::Threading;
using namespace Windows::Security::Credentials;
using namespace Windows::UI;
using namespace Windows::UI::Core;
using namespace Windows::UI::ViewManagement;
using namespace Windows::UI::Xaml;
using namespace Windows::UI::Xaml::Controls;
using namespace Windows::UI::Xaml::Media;
using namespace Windows::UI::Xaml::Media::Imaging;
using namespace Windows::Web::Http;
using namespace Windows::Web::Http::Filters;
using namespace Concurrency;
namespace muxc = Microsoft::UI::Xaml::Controls;

extern "C" void __cdecl horizon_uwp_attach_shell(CoreWindow^ window);
extern "C" void __cdecl horizon_uwp_open_game_view();
extern "C" int __cdecl horizon_uwp_prepare(const uint16_t*, size_t);
extern "C" int __cdecl horizon_uwp_pick_files(const wchar_t*, size_t, unsigned);
extern "C" int __cdecl horizon_uwp_pick_save_folder(const wchar_t*, size_t, unsigned);
extern "C" size_t __cdecl horizon_uwp_release_label(unsigned char*, size_t);
extern "C" int __cdecl horizon_uwp_probe_renderer(void*, unsigned, unsigned, unsigned);

namespace {
#ifdef HORIZON_UWP_SHELL_PROBE
void probe_stage(const wchar_t* value) {
    auto base = ApplicationData::Current->LocalFolder->Path;
    auto path = Platform::String::Concat(base, L"\\shell-stage.txt");
    HANDLE h = CreateFile2(path->Data(), GENERIC_WRITE, FILE_SHARE_READ, CREATE_ALWAYS, nullptr);
    if (h != INVALID_HANDLE_VALUE) { DWORD written = 0; WriteFile(h, value, static_cast<DWORD>(wcslen(value) * sizeof(wchar_t)), &written, nullptr); CloseHandle(h); }
}
#define PROBE_STAGE(value) probe_stage(value)
#else
#define PROBE_STAGE(value) ((void)0)
#endif
std::atomic<int> choice{-1};
std::atomic<unsigned> prompt_generation{0};
std::atomic<unsigned> skin_generation{0};
CoreDispatcher^ dispatcher;
Page^ page;
ContentControl^ content;
muxc::NavigationView^ navigation;
muxc::NavigationViewItem^ profile_navigation;
Button^ fallback_profile;
Platform::Object^ last_navigation_selection;
bool restoring_navigation = false;
ContentDialog^ active_dialog;
int dialog_result = -1;
Platform::String^ profile = L"Profile";
ImageSource^ profile_skin;
Platform::String^ account_labels;
std::string offline_input;
TextBox^ offline_name_box;
Platform::String^ prompt_text = L"";
unsigned prompt_kind = 0;
unsigned displayed_generation = 0;
bool renderer_known = false;
bool renderer_available = false;
TextBlock^ renderer_status;

int probe_native_renderer(CoreWindow^ window) {
    if (!window) return 0;
    const D3D_FEATURE_LEVEL requested[] = { D3D_FEATURE_LEVEL_9_1 };
    D3D_FEATURE_LEVEL selected = D3D_FEATURE_LEVEL_9_1;
    Microsoft::WRL::ComPtr<ID3D11Device> device;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> context;
    HRESULT result = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT, requested, ARRAYSIZE(requested), D3D11_SDK_VERSION,
        &device, &selected, &context);
    if (FAILED(result) || selected != D3D_FEATURE_LEVEL_9_1) return 0;
    Microsoft::WRL::ComPtr<IDXGIDevice> dxgi_device;
    Microsoft::WRL::ComPtr<IDXGIAdapter> adapter;
    Microsoft::WRL::ComPtr<IDXGIFactory2> factory;
    if (FAILED(device.As(&dxgi_device)) || FAILED(dxgi_device->GetAdapter(&adapter)) ||
        FAILED(adapter->GetParent(IID_PPV_ARGS(&factory)))) return 0;
    DXGI_SWAP_CHAIN_DESC1 description = {};
    description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    description.SampleDesc.Count = 1;
    description.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    description.BufferCount = 2;
    description.Scaling = DXGI_SCALING_NONE;
    description.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
    description.AlphaMode = DXGI_ALPHA_MODE_IGNORE;
    Microsoft::WRL::ComPtr<IUnknown> core_window;
    core_window = reinterpret_cast<IUnknown*>(window);
    Microsoft::WRL::ComPtr<IDXGISwapChain1> swap_chain;
    if (FAILED(factory->CreateSwapChainForCoreWindow(device.Get(), core_window.Get(),
        &description, nullptr, &swap_chain))) return 0;
    Microsoft::WRL::ComPtr<ID3D11Texture2D> back_buffer;
    Microsoft::WRL::ComPtr<ID3D11RenderTargetView> target;
    if (FAILED(swap_chain->GetBuffer(0, IID_PPV_ARGS(&back_buffer))) ||
        FAILED(device->CreateRenderTargetView(back_buffer.Get(), nullptr, &target))) return 0;
    return 1;
}
enum class AppAppearance { Automatic, Windows10, Windows10Fluent, Windows11SunValley };
AppAppearance selected_appearance = AppAppearance::Automatic;
AppAppearance startup_appearance = AppAppearance::Automatic;
bool show_settings = false;
bool show_logs = false;
bool show_news = false;
bool has_winui = false;
unsigned profile_kind = 0;
UISettings^ ui_settings;
AccessibilitySettings^ accessibility;

void refresh();
void update_navigation_surface(DependencyObject^ node);
void update_profile_navigation();
Platform::String^ decode(const unsigned char* bytes, size_t length);

Platform::String^ installed_release() {
    unsigned char buffer[256];
    const auto length = horizon_uwp_release_label(buffer, sizeof(buffer));
    return length && length <= sizeof(buffer) ? decode(buffer, length) : ref new Platform::String(L"Horizon");
}

Platform::String^ renderer_label() {
#ifdef HORIZON_UWP_NATIVE_D3D11
    return L"Direct3D 11 (native, feature level 9_1)";
#elif defined(HORIZON_UWP_DX12)
    return L"Direct3D 12 (WGPU)";
#else
    return L"OpenGL ES 3.0 (ANGLE)";
#endif
}

void load_latest_log(TextBox^ output) {
    output->Text = L"Loading the latest game log\u2026";
    auto root = ApplicationData::Current->LocalFolder->Path;
    auto path = Platform::String::Concat(root, L"\\game\\logs\\latest.log");
    ThreadPool::RunAsync(ref new WorkItemHandler([path, output](IAsyncAction^) {
        Platform::String^ result = L"No game log is available yet.";
        HANDLE file = CreateFile2(path->Data(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, OPEN_EXISTING, nullptr);
        if (file != INVALID_HANDLE_VALUE) {
            LARGE_INTEGER size = {};
            if (GetFileSizeEx(file, &size)) {
                constexpr DWORD limit = 65536;
                LARGE_INTEGER offset = {}; offset.QuadPart = size.QuadPart > limit ? size.QuadPart - limit : 0;
                if (SetFilePointerEx(file, offset, nullptr, FILE_BEGIN)) {
                    std::string bytes(limit, '\0'); DWORD count = 0;
                    if (ReadFile(file, &bytes[0], limit, &count, nullptr)) {
                        int needed = MultiByteToWideChar(CP_UTF8, 0, bytes.data(), count, nullptr, 0);
                        if (needed > 0) {
                            std::wstring wide(needed, L'\0');
                            MultiByteToWideChar(CP_UTF8, 0, bytes.data(), count, &wide[0], needed);
                            result = ref new Platform::String(wide.data(), needed);
                        } else if (count == 0) result = L"The latest game log is empty.";
                    }
                }
            }
            CloseHandle(file);
        }
        try { dispatcher->RunAsync(CoreDispatcherPriority::Normal, ref new DispatchedHandler([output, result]() { output->Text = result; })); }
        catch (...) {}
    }));
}

void load_installed_notes(TextBlock^ output) {
    try {
        auto package = Windows::ApplicationModel::Package::Current->InstalledLocation;
        auto context = task_continuation_context::use_current();
        create_task(package->GetFileAsync(L"horizon-release-notes.txt")).then([](StorageFile^ file) {
            return FileIO::ReadTextAsync(file);
        }, context).then([output](task<Platform::String^> pending) {
            try {
                auto body = pending.get();
                std::wstring clean; clean.reserve((std::min)(body->Length(), 32000u));
                bool tag = false;
                for (unsigned i = 0; i < body->Length() && clean.size() < 32000; ++i) {
                    const wchar_t ch = body->Data()[i];
                    if (ch == L'<') { tag = true; continue; }
                    if (ch == L'>') { tag = false; continue; }
                    if (!tag) clean.push_back(ch);
                }
                output->Text = ref new Platform::String(clean.data(), static_cast<unsigned>(clean.size()));
            } catch (...) { output->Text = L"Release notes were not included with this build."; }
        }, context);
    } catch (...) { output->Text = L"Release notes were not included with this build."; }
}

bool supports_acrylic() {
    auto version = Windows::System::Profile::AnalyticsInfo::VersionInfo->DeviceFamilyVersion;
    const auto packed = _wcstoui64(version->Data(), nullptr, 10);
    return ((packed >> 16) & 0xffff) >= 16299 &&
        Windows::Foundation::Metadata::ApiInformation::IsTypePresent(L"Windows.UI.Xaml.Media.AcrylicBrush");
}
bool supports_mica() {
    auto version = Windows::System::Profile::AnalyticsInfo::VersionInfo->DeviceFamilyVersion;
    return ((_wcstoui64(version->Data(), nullptr, 10) >> 16) & 0xffff) >= 22000;
}
AppAppearance resolved_appearance(AppAppearance requested) {
    if (requested == AppAppearance::Windows10) return requested;
    if (requested == AppAppearance::Windows10Fluent && supports_acrylic()) return requested;
    if (requested == AppAppearance::Windows11SunValley && supports_mica()) return requested;
    if (supports_mica()) return AppAppearance::Windows11SunValley;
    if (supports_acrylic()) return AppAppearance::Windows10Fluent;
    return AppAppearance::Windows10;
}
bool dark_mode() {
    const auto color = ui_settings->GetColorValue(UIColorType::Background);
    return static_cast<int>(color.R) + color.G + color.B < 384;
}
Color color(unsigned value) {
    return ColorHelper::FromArgb(255, (value >> 16) & 255, (value >> 8) & 255, value & 255);
}
Brush^ surface(bool card) {
    if (accessibility && accessibility->HighContrast)
        return ref new SolidColorBrush(ui_settings->GetColorValue(UIColorType::Background));
    const bool dark = dark_mode();
    const auto appearance = resolved_appearance(selected_appearance);
    const unsigned solid = dark ? (card ? 0x282828 : 0x202020) : (card ? 0xffffff : 0xf2f2f2);
    if (appearance == AppAppearance::Windows10Fluent && supports_acrylic()) {
        auto brush = ref new AcrylicBrush();
        brush->BackgroundSource = card ? AcrylicBackgroundSource::Backdrop : AcrylicBackgroundSource::HostBackdrop;
        brush->TintColor = color(dark ? (card ? 0x282828 : 0x202020) : 0xffffff);
        brush->TintOpacity = dark ? (card ? 0.8 : 0.6) : (card ? 0.75 : 0.5);
        brush->FallbackColor = color(solid);
        return brush;
    }
    if (appearance == AppAppearance::Windows11SunValley && card)
        return ref new SolidColorBrush(color(dark ? 0x2c2c2c : 0xffffff));
    return ref new SolidColorBrush(color(solid));
}
double card_radius() {
    const auto appearance = resolved_appearance(selected_appearance);
    return appearance == AppAppearance::Windows10 ? 0 : appearance == AppAppearance::Windows10Fluent ? 2 : 8;
}

Platform::String^ decode(const unsigned char* bytes, size_t length) {
    if (!bytes || !length || length > 4096) return nullptr;
    int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
        reinterpret_cast<const char*>(bytes), static_cast<int>(length), nullptr, 0);
    if (count <= 0) return nullptr;
    std::wstring buffer(count, L'\0');
    if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
        reinterpret_cast<const char*>(bytes), static_cast<int>(length), &buffer[0], count) != count) return nullptr;
    return ref new Platform::String(buffer.data(), count);
}

void choose(int value, unsigned generation) {
    if (generation != prompt_generation.load()) return;
    int expected = 0;
    choice.compare_exchange_strong(expected, value);
}

Button^ button(Platform::String^ label, int value) {
    auto result = ref new Button();
    result->Content = label;
    result->Margin = Thickness(0, 8, 0, 0);
    if (value == 1 && renderer_known && !renderer_available) result->IsEnabled = false;
    const auto generation = displayed_generation;
    result->Click += ref new RoutedEventHandler([value, generation](Platform::Object^, RoutedEventArgs^) { choose(value, generation); });
    return result;
}

TextBlock^ text(Platform::String^ value, double size) {
    auto result = ref new TextBlock();
    result->Text = value;
    result->FontSize = size;
    result->TextWrapping = TextWrapping::Wrap;
    result->Margin = Thickness(0, 0, 0, 12);
    return result;
}

Border^ card(StackPanel^ contents) {
    auto border = ref new Border();
    border->Background = surface(true);
    border->CornerRadius = CornerRadius(card_radius());
    border->Padding = Thickness(16);
    border->Margin = Thickness(0, 0, 0, 20);
    try {
        auto resources = Application::Current->Resources;
        if (resources->HasKey(L"CardStrokeColorDefaultBrush")) {
            border->BorderBrush = safe_cast<Brush^>(resources->Lookup(L"CardStrokeColorDefaultBrush"));
            border->BorderThickness = Thickness(1);
        }
    } catch (Platform::Exception^) {}
    border->Child = contents;
    return border;
}

Border^ profile_card() {
    auto layout = ref new Grid();
    auto icon_column = ref new ColumnDefinition(); icon_column->Width = GridLength(1, GridUnitType::Auto);
    auto text_column = ref new ColumnDefinition(); text_column->Width = GridLength(1, GridUnitType::Star);
    auto action_column = ref new ColumnDefinition(); action_column->Width = GridLength(1, GridUnitType::Auto);
    layout->ColumnDefinitions->Append(icon_column); layout->ColumnDefinitions->Append(text_column); layout->ColumnDefinitions->Append(action_column);
    FrameworkElement^ icon;
    if (profile_skin) {
        auto image = ref new Image(); image->Source = profile_skin; image->Stretch = Stretch::Fill; icon = image;
    } else {
        auto placeholder = ref new FontIcon(); placeholder->Glyph = L"\uE77B"; placeholder->FontFamily = ref new FontFamily(L"Segoe MDL2 Assets");
        placeholder->FontSize = 24; placeholder->Opacity = 0.5; icon = placeholder;
    }
    icon->Width = 48; icon->Height = 48;
    layout->Children->Append(icon);
    auto labels = ref new StackPanel(); labels->Margin = Thickness(12, 0, 0, 0); labels->VerticalAlignment = VerticalAlignment::Center;
    auto title = text(profile_kind ? profile : L"Not Signed In", 14);
    title->FontWeight = Windows::UI::Text::FontWeights::SemiBold; title->Margin = Thickness(0);
    auto subtitle = text(profile_kind == 1 ? L"Minecraft: Java Edition account" :
        profile_kind == 2 ? L"Offline account" : profile_kind == 3 ? L"Saved Microsoft account" :
        L"Connect your Microsoft account or add an offline account", 12);
    subtitle->Opacity = 0.7; subtitle->Margin = Thickness(0);
    labels->Children->Append(title); labels->Children->Append(subtitle);
    Grid::SetColumn(labels, 1); layout->Children->Append(labels);
    auto manage = button(profile_kind ? L"Manage" : L"Sign In", 2);
    manage->Margin = Thickness(15, 0, 0, 0); manage->VerticalAlignment = VerticalAlignment::Center;
    Grid::SetColumn(manage, 2); layout->Children->Append(manage);
    auto contents = ref new StackPanel(); contents->Children->Append(layout);
    return card(contents);
}

void update_profile_navigation() {
    auto label = profile_kind ? profile : ref new Platform::String(L"Account");
    if (profile_navigation) {
        profile_navigation->Content = label;
        if (profile_skin) {
            auto icon = ref new muxc::ImageIcon(); icon->Source = profile_skin; profile_navigation->Icon = icon;
        } else profile_navigation->Icon = ref new SymbolIcon(Symbol::Contact);
    }
    if (fallback_profile) fallback_profile->Content = label;
}

bool valid_skin_url(Platform::String^ value) {
    if (!value) return false;
    const std::wstring url(value->Data(), value->Length());
    const std::wstring prefix = L"https://textures.minecraft.net/texture/";
    if (url.compare(0, prefix.size(), prefix) != 0 || url.size() <= prefix.size() || url.size() > prefix.size() + 128) return false;
    for (size_t i = prefix.size(); i < url.size(); ++i) {
        const auto c = url[i];
        if (!((c >= L'0' && c <= L'9') || (c >= L'a' && c <= L'f') || (c >= L'A' && c <= L'F'))) return false;
    }
    return true;
}

void load_profile_skin(Platform::String^ value, unsigned generation) {
    if (!valid_skin_url(value)) return;
    try {
        auto filter = ref new HttpBaseProtocolFilter(); filter->AllowAutoRedirect = false;
        auto client = ref new HttpClient(filter);
        auto stream = ref new InMemoryRandomAccessStream();
        auto context = task_continuation_context::use_current();
        create_task(client->GetBufferAsync(ref new Uri(value))).then([stream, client](IBuffer^ bytes) {
            if (!bytes || bytes->Length == 0 || bytes->Length > 1024 * 1024) throw ref new Platform::FailureException();
            auto writer = ref new DataWriter(stream); writer->WriteBuffer(bytes);
            return create_task(writer->StoreAsync()).then([writer](unsigned) { writer->DetachStream(); });
        }, context).then([stream]() {
            stream->Seek(0);
            return create_task(BitmapDecoder::CreateAsync(stream));
        }, context).then([](BitmapDecoder^ decoder) {
            if (decoder->PixelWidth != 64 || (decoder->PixelHeight != 32 && decoder->PixelHeight != 64))
                throw ref new Platform::FailureException();
            auto transform = ref new BitmapTransform();
            return create_task(decoder->GetPixelDataAsync(BitmapPixelFormat::Bgra8, BitmapAlphaMode::Premultiplied,
                transform, ExifOrientationMode::IgnoreExifOrientation, ColorManagementMode::DoNotColorManage));
        }, context).then([generation](task<PixelDataProvider^> pending) {
            try {
                auto pixels = pending.get()->DetachPixelData();
                if (generation != skin_generation.load() || !pixels || pixels->Length < 64 * 32 * 4) return;
                auto image = ref new WriteableBitmap(8, 8);
                Microsoft::WRL::ComPtr<IBufferByteAccess> access;
                auto buffer = image->PixelBuffer;
                if (FAILED(reinterpret_cast<IInspectable*>(buffer)->QueryInterface(IID_PPV_ARGS(&access)))) return;
                byte* dest = nullptr;
                if (FAILED(access->Buffer(&dest)) || !dest) return;
                for (unsigned y = 0; y < 8; ++y) for (unsigned x = 0; x < 8; ++x) {
                    const unsigned base = ((y + 8) * 64 + x + 8) * 4;
                    const unsigned overlay = ((y + 8) * 64 + x + 40) * 4;
                    const unsigned alpha = pixels[overlay + 3];
                    for (unsigned c = 0; c < 4; ++c)
                        dest[(y * 8 + x) * 4 + c] = static_cast<byte>((std::min)(255u,
                            static_cast<unsigned>(pixels[overlay + c]) + (static_cast<unsigned>(pixels[base + c]) * (255 - alpha) + 127) / 255));
                }
                image->Invalidate();
                profile_skin = image;
                update_profile_navigation();
                if (prompt_kind == 0) refresh();
            } catch (...) { /* Keep the fallback icon when skin loading fails. */ }
        }, context);
    } catch (...) { /* The account remains usable without its skin. */ }
}

void show_prompt_dialog(Platform::String^ message, Platform::String^ accounts, unsigned kind, unsigned generation) {
    auto dialog = ref new ContentDialog();
    dialog->Title = (kind == 1 || kind == 5) ? L"Account Management" :
        kind == 2 ? L"Microsoft sign-in" : L"Horizon";
    dialog->PrimaryButtonText = kind == 3 ? L"OK" : L"Cancel";
    auto body = ref new StackPanel();
    body->Children->Append(text(message, 16));
    auto add_action = [dialog, body, generation](const wchar_t* label, int value) {
        auto action = ref new Button(); action->Content = ref new Platform::String(label);
        action->HorizontalAlignment = HorizontalAlignment::Stretch;
        action->Margin = Thickness(0, 0, 0, 10);
        action->Click += ref new RoutedEventHandler([dialog, value, generation](Platform::Object^, RoutedEventArgs^) {
            if (generation != prompt_generation.load()) return;
            if (offline_name_box && value == 1) {
                auto name = offline_name_box->Text;
                int needed = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, name->Data(), name->Length(), nullptr, 0, nullptr, nullptr);
                offline_input.clear();
                if (needed > 0 && needed <= 64) {
                    offline_input.resize(needed);
                    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, name->Data(), name->Length(), &offline_input[0], needed, nullptr, nullptr);
                }
            }
            dialog_result = value;
            dialog->Hide();
        });
        body->Children->Append(action);
    };
    offline_name_box = nullptr;
    switch (kind) {
    case 1:
        add_action(L"Sign in with Microsoft", 1); add_action(L"Choose an account", 2);
        add_action(L"Add an offline account", 3); add_action(L"Remove an account", 4); break;
    case 2: break;
    case 3: break;
    case 4: add_action(L"Remove account", 1); break;
    case 5: {
        std::wstring labels = accounts ? accounts->Data() : L"";
        size_t start = 0; int index = 0;
        while (start < labels.size() && index < 16) {
            const auto end = labels.find(L'\n', start);
            auto label = labels.substr(start, end == std::wstring::npos ? end : end - start);
            const auto separator = label.find(L'\t');
            if (separator != std::wstring::npos) label.replace(separator, 1, L" \u2014 ");
            add_action(label.c_str(), 100 + index++);
            if (end == std::wstring::npos) break;
            start = end + 1;
        }
        break;
    }
    case 6: add_action(L"Continue", 1); break;
    case 7:
        offline_input.clear();
        offline_name_box = ref new TextBox(); offline_name_box->Header = L"Player name"; offline_name_box->MaxLength = 16;
#ifdef HORIZON_UWP_SHELL_PROBE
        offline_name_box->Text = L"MockUser";
#endif
        body->Children->Append(offline_name_box); add_action(L"Add account", 1); break;
    default: choose(-2, generation); return;
    }
    dialog->Content = body;
    dialog_result = kind == 3 ? 1 : -1;
    active_dialog = dialog;
    dialog->Closed += ref new TypedEventHandler<ContentDialog^, ContentDialogClosedEventArgs^>(
        [generation](ContentDialog^, ContentDialogClosedEventArgs^) {
            const int result = dialog_result;
            active_dialog = nullptr;
            offline_name_box = nullptr;
            choose(result, generation);
        });
    dialog->ShowAsync();
}

void show_message(Platform::String^ title, Platform::String^ message) {
    auto dialog = ref new ContentDialog();
    dialog->Title = title;
    dialog->Content = text(message, 15);
    dialog->PrimaryButtonText = L"Close";
    dialog->ShowAsync();
}

void import_content(unsigned kind) {
    try {
        auto context = task_continuation_context::use_current();
        create_task(ApplicationData::Current->LocalFolder->CreateFolderAsync(L"game", CreationCollisionOption::OpenIfExists))
            .then([kind](StorageFolder^ game) {
                return game->CreateFolderAsync(kind == 1 ? L"horizon-mods" : L"resourcepacks", CreationCollisionOption::OpenIfExists);
            }, context).then([kind](task<StorageFolder^> result) {
                try {
                    auto folder = result.get();
                    auto path = folder->Path;
                    if (horizon_uwp_pick_files(path->Data(), path->Length(), kind) != 0)
                        show_message(L"Import content", L"The file picker could not be opened.");
                } catch (...) { show_message(L"Import content", L"The content folder could not be opened."); }
            }, context);
    } catch (...) { show_message(L"Import content", L"The content folder could not be opened."); }
}

std::uint64_t directory_bytes(const std::wstring& root, unsigned depth = 0) {
    if (depth > 64) return 0;
    std::wstring pattern = root;
    if (!pattern.empty() && pattern.back() != L'\\') pattern.push_back(L'\\');
    pattern.push_back(L'*');
    WIN32_FIND_DATAW entry = {};
    HANDLE find = FindFirstFileW(pattern.c_str(), &entry);
    if (find == INVALID_HANDLE_VALUE) return 0;
    std::uint64_t total = 0;
    do {
        if (!wcscmp(entry.cFileName, L".") || !wcscmp(entry.cFileName, L"..")) continue;
        if (entry.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) continue;
        if (entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
            std::wstring child = root;
            if (!child.empty() && child.back() != L'\\') child.push_back(L'\\');
            child += entry.cFileName;
            total += directory_bytes(child, depth + 1);
        } else {
            total += (static_cast<std::uint64_t>(entry.nFileSizeHigh) << 32) | entry.nFileSizeLow;
        }
    } while (FindNextFileW(find, &entry));
    FindClose(find);
    return total;
}
std::wstring format_bytes(std::uint64_t bytes) {
    const wchar_t* units[] = { L"B", L"KiB", L"MiB", L"GiB", L"TiB" };
    double value = static_cast<double>(bytes);
    unsigned unit = 0;
    while (value >= 1024.0 && unit + 1 < _countof(units)) { value /= 1024.0; ++unit; }
    wchar_t formatted[64];
    if (unit == 0) swprintf_s(formatted, L"%llu %s", static_cast<unsigned long long>(bytes), units[unit]);
    else swprintf_s(formatted, L"%.1f %s", value, units[unit]);
    return formatted;
}
void count_content(Platform::String^ name, TextBlock^ output) {
    try {
        auto base = ApplicationData::Current->LocalFolder->Path;
        auto path = Platform::String::Concat(Platform::String::Concat(base, L"\\game\\"), name);
        auto context = task_continuation_context::use_current();
        create_task(StorageFolder::GetFolderFromPathAsync(path)).then([](StorageFolder^ folder) {
            return folder->GetItemsAsync();
        }, context).then([output, path](task<Windows::Foundation::Collections::IVectorView<IStorageItem^>^> pending) {
            try {
                const auto count = pending.get()->Size;
                const auto bytes = directory_bytes(std::wstring(path->Data(), path->Length()));
                wchar_t value[128];
                swprintf_s(value, L"%u item%s · %s", count, count == 1 ? L"" : L"s", format_bytes(bytes).c_str());
                output->Text = ref new Platform::String(value);
            } catch (...) { output->Text = L"No items yet or folder unavailable."; }
        }, context);
    } catch (...) { output->Text = L"Storage information is unavailable."; }
}
Button^ action_button(Platform::String^ label, RoutedEventHandler^ handler);
void refresh_save_list(StackPanel^ output) {
    output->Children->Clear();
    output->Children->Append(text(L"Calculating save sizes…", 14));
    auto local = ApplicationData::Current->LocalFolder->Path;
    auto root = Platform::String::Concat(Platform::String::Concat(local, L"\\game\\"), L"saves");
    ThreadPool::RunAsync(ref new WorkItemHandler([output, root](IAsyncAction^) {
        struct SaveInfo { std::wstring name; std::wstring path; std::uint64_t bytes; };
        std::vector<SaveInfo> saves;
        const std::wstring save_root(root->Data(), root->Length());
        std::wstring pattern = save_root + L"\\*";
        WIN32_FIND_DATAW entry = {};
        HANDLE find = FindFirstFileW(pattern.c_str(), &entry);
        std::uint64_t total = 0;
        if (find != INVALID_HANDLE_VALUE) {
            do {
                if (!(entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) ||
                    (entry.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) ||
                    !wcscmp(entry.cFileName, L".") || !wcscmp(entry.cFileName, L"..")) continue;
                SaveInfo item; item.name = entry.cFileName; item.path = save_root + L"\\" + item.name;
                item.bytes = directory_bytes(item.path); total += item.bytes; saves.push_back(std::move(item));
            } while (FindNextFileW(find, &entry));
            FindClose(find);
        }
        try {
            dispatcher->RunAsync(CoreDispatcherPriority::Normal, ref new DispatchedHandler([output, saves, total]() {
                output->Children->Clear();
                output->Children->Append(action_button(L"Import save", ref new RoutedEventHandler([](Platform::Object^, RoutedEventArgs^) {
                    auto local = ApplicationData::Current->LocalFolder->Path;
                    create_task(ApplicationData::Current->LocalFolder->CreateFolderAsync(L"game", CreationCollisionOption::OpenIfExists)).then([](StorageFolder^ game) {
                        return game->CreateFolderAsync(L"saves", CreationCollisionOption::OpenIfExists);
                    }).then([](task<StorageFolder^> pending) {
                        try {
                            auto saves = pending.get(); auto path = saves->Path;
                            if (horizon_uwp_pick_save_folder(path->Data(), path->Length(), 1) != 0)
                                show_message(L"Import save", L"The save folder picker could not be opened.");
                        } catch (...) { show_message(L"Import save", L"The app's saves folder could not be opened."); }
                    });
                })));
                if (saves.empty()) output->Children->Append(text(L"No worlds yet.", 14));
                for (const auto& save : saves) {
                    auto row = ref new StackPanel(); row->Orientation = Orientation::Horizontal;
                    auto label = text(ref new Platform::String((save.name + L" · " + format_bytes(save.bytes)).c_str()), 14);
                    label->VerticalAlignment = VerticalAlignment::Center;
                    row->Children->Append(label);
                    auto path = save.path;
                    row->Children->Append(action_button(L"Export", ref new RoutedEventHandler([path](Platform::Object^, RoutedEventArgs^) {
                        if (horizon_uwp_pick_save_folder(path.c_str(), path.size(), 2) != 0)
                            show_message(L"Export save", L"The save folder picker could not be opened.");
                    })));
                    output->Children->Append(row);
                }
                output->Children->Append(text(ref new Platform::String((L"Total saves: " + format_bytes(total)).c_str()), 14));
            }));
        } catch (...) {}
    }));
}

bool clear_directory_contents(const std::wstring& root, unsigned depth = 0) {
    if (depth > 64) return false;
    std::wstring pattern = root;
    if (!pattern.empty() && pattern.back() != L'\\') pattern.push_back(L'\\');
    pattern.push_back(L'*');
    WIN32_FIND_DATAW entry = {};
    HANDLE find = FindFirstFileW(pattern.c_str(), &entry);
    if (find == INVALID_HANDLE_VALUE) return GetLastError() == ERROR_FILE_NOT_FOUND || GetLastError() == ERROR_PATH_NOT_FOUND;
    bool success = true;
    do {
        if (!wcscmp(entry.cFileName, L".") || !wcscmp(entry.cFileName, L"..")) continue;
        std::wstring child = root;
        if (!child.empty() && child.back() != L'\\') child.push_back(L'\\');
        child += entry.cFileName;
        const bool directory = (entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
        const bool link = (entry.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
        if (directory && !link) {
            success = clear_directory_contents(child, depth + 1) && success;
            success = (RemoveDirectoryW(child.c_str()) != 0) && success;
        } else if (directory) {
            success = (RemoveDirectoryW(child.c_str()) != 0) && success;
        } else {
            SetFileAttributesW(child.c_str(), FILE_ATTRIBUTE_NORMAL);
            success = (DeleteFileW(child.c_str()) != 0) && success;
        }
    } while (FindNextFileW(find, &entry));
    FindClose(find);
    return success;
}

bool reset_owned_data() {
    try {
        auto vault = ref new PasswordVault();
        try {
            auto entries = vault->FindAllByResource(L"dev.playhorizon.uwp.accounts");
            for (unsigned i = 0; i < entries->Size; ++i) vault->Remove(entries->GetAt(i));
        } catch (Platform::Exception^ error) {
            if (error->HResult != HRESULT_FROM_WIN32(ERROR_NOT_FOUND)) return false;
        }
        auto app = ApplicationData::Current;
        const std::wstring roots[] = {
            std::wstring(app->LocalFolder->Path->Data()),
            std::wstring(app->RoamingFolder->Path->Data()),
            std::wstring(app->LocalCacheFolder->Path->Data()),
            std::wstring(app->TemporaryFolder->Path->Data()),
        };
        for (const auto& root : roots) if (!clear_directory_contents(root)) return false;
        try {
            dispatcher->RunAsync(CoreDispatcherPriority::Normal, ref new DispatchedHandler([]() {
                ApplicationData::Current->LocalSettings->Values->Clear();
                ApplicationData::Current->RoamingSettings->Values->Clear();
                CoreApplication::Exit();
            }));
        } catch (...) { return false; }
        return true;
    } catch (...) { return false; }
}

void confirm_reset_data() {
    auto dialog = ref new ContentDialog();
    dialog->Title = L"Reset all Horizon data?";
    dialog->Content = text(L"This deletes this app's worlds, settings, logs, cache and saved accounts. It does not delete the separately installed shared Minecraft assets package or any exported copies.", 15);
    dialog->PrimaryButtonText = L"Reset and close";
    dialog->CloseButtonText = L"Cancel";
    auto result = dialog->ShowAsync();
    result->Completed = ref new Windows::Foundation::AsyncOperationCompletedHandler<ContentDialogResult>([](IAsyncOperation<ContentDialogResult>^ pending, Windows::Foundation::AsyncStatus status) {
        if (status != Windows::Foundation::AsyncStatus::Completed || pending->GetResults() != ContentDialogResult::Primary) return;
        ThreadPool::RunAsync(ref new WorkItemHandler([](IAsyncAction^) {
            if (!reset_owned_data()) {
                try { dispatcher->RunAsync(CoreDispatcherPriority::Normal, ref new DispatchedHandler([]() {
                    show_message(L"Reset failed", L"Some app data could not be removed. No shared assets package was changed.");
                })); } catch (...) {}
            }
        }));
    });
}
void refresh_app_storage(TextBlock^ output) {
    output->Text = L"Calculating app storage…";
    auto local = ApplicationData::Current->LocalFolder->Path;
    auto roaming = ApplicationData::Current->RoamingFolder->Path;
    auto cache = ApplicationData::Current->LocalCacheFolder->Path;
    auto temporary = ApplicationData::Current->TemporaryFolder->Path;
    auto installed = Windows::ApplicationModel::Package::Current->InstalledLocation->Path;
    ThreadPool::RunAsync(ref new WorkItemHandler([output, local, roaming, cache, temporary, installed](IAsyncAction^) {
        std::uint64_t framework = 0;
        try {
            auto dependencies = Windows::ApplicationModel::Package::Current->Dependencies;
            for (unsigned i = 0; i < dependencies->Size; ++i) {
                auto dependency = dependencies->GetAt(i);
                auto name = dependency->Id->Name;
                if (name->Length() >= 14 && wcsncmp(name->Data(), L"Horizon.Asset.", 14) == 0 &&
                    wcscmp(dependency->Id->Publisher->Data(), L"CN=HorizonUWPPrototype") == 0)
                    framework += directory_bytes(std::wstring(dependency->InstalledLocation->Path->Data()));
            }
        } catch (...) {}
        const auto local_size = directory_bytes(std::wstring(local->Data()));
        const auto roaming_size = directory_bytes(std::wstring(roaming->Data()));
        const auto cache_size = directory_bytes(std::wstring(cache->Data()));
        const auto temp_size = directory_bytes(std::wstring(temporary->Data()));
        const auto app_data = local_size + roaming_size + cache_size + temp_size;
        const auto client_package = directory_bytes(std::wstring(installed->Data()));
        const auto combined = app_data + client_package + framework;
        std::wstring result = L"App data (local, roaming, cache and temporary): " + format_bytes(app_data) + L"\nInstalled Horizon client: " + format_bytes(client_package) + L"  ·  Shared Horizon assets: " + format_bytes(framework) + L"\nCombined app data and installed packages: " + format_bytes(combined) + L"\nSecure account entries are stored by Windows and are not included in file-size totals.";
        try { dispatcher->RunAsync(CoreDispatcherPriority::Normal, ref new DispatchedHandler([output, result]() { output->Text = ref new Platform::String(result.c_str()); })); }
        catch (...) {}
    }));
}
Button^ action_button(Platform::String^ label, RoutedEventHandler^ handler) {
    auto result = ref new Button();
    result->Content = label;
    result->Margin = Thickness(0, 8, 0, 0);
    result->Click += handler;
    return result;
}

void apply_theme() {
    if (!page) return;
    try {
        if (has_winui) muxc::BackdropMaterial::SetApplyToRootOrPageBackground(page, false);
        page->ClearValue(Control::BackgroundProperty);
        if (accessibility && accessibility->HighContrast) {
            page->Background = surface(false);
            if (navigation) update_navigation_surface(navigation);
            return;
        }
        const auto appearance = resolved_appearance(selected_appearance);
        if (appearance == AppAppearance::Windows11SunValley && has_winui && supports_mica()) {
            muxc::BackdropMaterial::SetApplyToRootOrPageBackground(page, true);
        } else page->Background = surface(false);
        if (navigation) update_navigation_surface(navigation);
    } catch (Platform::Exception^) {
        page->ClearValue(Control::BackgroundProperty);
    }
}

void update_navigation_surface(DependencyObject^ node) {
    if (!node) return;
    auto element = dynamic_cast<FrameworkElement^>(node);
    if (element) {
        auto split = dynamic_cast<SplitView^>(element);
        if (split && element->Name == L"RootSplitView") {
            split->PaneBackground = resolved_appearance(selected_appearance) == AppAppearance::Windows11SunValley
                && (!accessibility || !accessibility->HighContrast) ? nullptr : surface(false);
            auto content_grid = dynamic_cast<Grid^>(split->Content);
            if (content_grid && content_grid->Name == L"ContentGrid") {
                const auto appearance = resolved_appearance(selected_appearance);
                content_grid->Background = appearance == AppAppearance::Windows10
                    || (accessibility && accessibility->HighContrast) ? surface(false)
                    : ref new SolidColorBrush(Colors::Transparent);
            }
            return;
        }
        if (dynamic_cast<ContentPresenter^>(element) && element->Name == L"ContentPresenter") return;
    }
    const int count = VisualTreeHelper::GetChildrenCount(node);
    for (int i = 0; i < count; ++i) update_navigation_surface(VisualTreeHelper::GetChild(node, i));
}

void refresh() {
    if (!content) return;
    auto panel = ref new StackPanel();
    panel->Margin = Thickness(24);
    panel->MaxWidth = 780;
    panel->HorizontalAlignment = HorizontalAlignment::Stretch;
    if (show_settings && prompt_kind == 0) {
        panel->Children->Append(text(L"Settings", 28));
        panel->Children->Append(profile_card());
        auto appearance_card = ref new StackPanel();
        appearance_card->Children->Append(text(L"Appearance", 18));
        auto themes = ref new ComboBox();
        themes->Header = L"App style";
        for (auto label : { L"Automatic", L"Windows 10", L"Windows 10 (Fluent)", L"Windows 11 (Sun Valley)" }) {
            auto item = ref new ComboBoxItem(); item->Content = ref new Platform::String(label);
            themes->Items->Append(item);
        }
        safe_cast<ComboBoxItem^>(themes->Items->GetAt(2))->IsEnabled = supports_acrylic();
        safe_cast<ComboBoxItem^>(themes->Items->GetAt(3))->IsEnabled = supports_mica() && has_winui;
        themes->SelectedIndex = static_cast<int>(selected_appearance);
        themes->SelectionChanged += ref new SelectionChangedEventHandler([themes](Platform::Object^, SelectionChangedEventArgs^) {
            if (themes->SelectedIndex < 0 || themes->SelectedIndex > 3) return;
            selected_appearance = static_cast<AppAppearance>(themes->SelectedIndex);
            ApplicationData::Current->LocalSettings->Values->Insert(L"pregame-theme", PropertyValue::CreateInt32(themes->SelectedIndex));
            apply_theme();
            dispatcher->RunAsync(CoreDispatcherPriority::Normal, ref new DispatchedHandler([]() { refresh(); }));
        });
        appearance_card->Children->Append(themes);
        if ((resolved_appearance(selected_appearance) == AppAppearance::Windows11SunValley) !=
            (resolved_appearance(startup_appearance) == AppAppearance::Windows11SunValley))
            appearance_card->Children->Append(text(L"Colours and materials have updated. Restart the app to finish applying the control styles.", 14));
        appearance_card->Children->Append(text(L"Automatic matches your Windows version. The app follows your light, dark and high contrast settings.", 14));
        panel->Children->Append(card(appearance_card));
        auto launch_card = ref new StackPanel();
        launch_card->Children->Append(text(L"Launch settings", 18));
        auto renderers = ref new ComboBox(); renderers->Header = L"Renderer";
        auto angle = ref new ComboBoxItem();
        angle->Content = renderer_known && renderer_available ? renderer_label() :
            renderer_known ? L"No compatible renderer detected" : L"Checking this device…";
        angle->IsEnabled = renderer_known && renderer_available;
        renderers->Items->Append(angle);
        renderers->SelectedIndex = 0;
        launch_card->Children->Append(renderers);
        renderer_status = text(renderer_known
            ? (renderer_available ? L"The client configured and presented through this renderer on this device." : L"The client could not open a compatible graphics device and presentation surface on this device.")
            : L"The client is checking the graphics adapters available to this app.", 14);
        launch_card->Children->Append(renderer_status);
        panel->Children->Append(card(launch_card));
        panel->Children->Append(text(L"Storage", 18));
        auto worlds_card = ref new StackPanel();
        worlds_card->Children->Append(text(L"Worlds and saves", 16));
        worlds_card->Children->Append(text(L"Import and export make a copy. Existing saves are never replaced.", 14));
        auto save_rows = ref new StackPanel();
        worlds_card->Children->Append(save_rows);
        panel->Children->Append(card(worlds_card)); refresh_save_list(save_rows);
        auto mods_card = ref new StackPanel();
        mods_card->Children->Append(text(L"Mods", 16));
        auto mods_count = text(L"Checking storage\u2026", 14); mods_card->Children->Append(mods_count);
        mods_card->Children->Append(action_button(L"Import mods", ref new RoutedEventHandler([](Platform::Object^, RoutedEventArgs^) { import_content(1); })));
        panel->Children->Append(card(mods_card)); count_content(L"horizon-mods", mods_count);
        auto packs_card = ref new StackPanel();
        packs_card->Children->Append(text(L"Resource packs", 16));
        auto packs_count = text(L"Checking storage\u2026", 14); packs_card->Children->Append(packs_count);
        packs_card->Children->Append(action_button(L"Import resource packs", ref new RoutedEventHandler([](Platform::Object^, RoutedEventArgs^) { import_content(2); })));
        panel->Children->Append(card(packs_card)); count_content(L"resourcepacks", packs_count);
        auto assets_card = ref new StackPanel();
        assets_card->Children->Append(text(L"Minecraft assets", 16));
        assets_card->Children->Append(text(L"Base assets are provided by the app or its reusable resource framework.", 14));
        panel->Children->Append(card(assets_card));
        auto storage_card = ref new StackPanel();
        storage_card->Children->Append(text(L"Storage management", 16));
        auto storage_total = text(L"Calculating app and shared asset sizes…", 14);
        storage_card->Children->Append(storage_total);
        storage_card->Children->Append(action_button(L"Reset all app data", ref new RoutedEventHandler([](Platform::Object^, RoutedEventArgs^) { confirm_reset_data(); })));
        panel->Children->Append(card(storage_card)); refresh_app_storage(storage_total);
        auto about_card = ref new StackPanel();
        about_card->Children->Append(text(L"About", 18));
        about_card->Children->Append(action_button(L"About Horizon UWP", ref new RoutedEventHandler([](Platform::Object^, RoutedEventArgs^) {
            const auto version = Windows::ApplicationModel::Package::Current->Id->Version;
            auto release = installed_release();
            wchar_t message[512];
            swprintf_s(message, L"Horizon UWP\n%s\nPackage version %u.%u.%u.%u\n\nAn unofficial UWP port of Horizon for Minecraft: Java Edition.",
                release->Data(), version.Major, version.Minor, version.Build, version.Revision);
            show_message(L"About Horizon UWP", ref new Platform::String(message));
        })));
        panel->Children->Append(card(about_card));
    } else if (show_logs && prompt_kind == 0) {
        panel->Children->Append(text(L"Logs", 28));
        panel->Children->Append(text(L"Latest game log", 18));
        auto log = ref new TextBox(); log->IsReadOnly = true; log->AcceptsReturn = true;
        log->TextWrapping = TextWrapping::Wrap; log->MinHeight = 240;
        panel->Children->Append(log);
        panel->Children->Append(action_button(L"Refresh", ref new RoutedEventHandler([log](Platform::Object^, RoutedEventArgs^) { load_latest_log(log); })));
        load_latest_log(log);
    } else if (show_news && prompt_kind == 0) {
        panel->Children->Append(text(L"What's new", 28));
        panel->Children->Append(text(installed_release(), 18));
        auto notes = text(L"Loading release notes\u2026", 14);
        panel->Children->Append(notes);
        load_installed_notes(notes);
    } else if (prompt_kind == 0) {
        auto logo = ref new Image();
        auto logo_bitmap = ref new BitmapImage();
        logo_bitmap->UriSource = ref new Uri(L"ms-appx:///Assets/Square150x150Logo.png");
        logo->Source = logo_bitmap; logo->Width = 64; logo->Height = 64;
        logo->Margin = Thickness(0, 0, 0, 12);
        panel->Children->Append(logo);
        panel->Children->Append(text(L"Horizon", 32));
        panel->Children->Append(text(prompt_text, 16));
        panel->Children->Append(text(renderer_known
            ? (renderer_available ? Platform::String::Concat(renderer_label(), L" is ready.") : L"No compatible renderer was detected.")
            : L"Checking graphics support…", 14));
        panel->Children->Append(profile_card());
        panel->Children->Append(button(L"Start game", 1));
    }
    auto scroll = ref new ScrollViewer();
    scroll->HorizontalScrollMode = ScrollMode::Disabled;
    scroll->HorizontalScrollBarVisibility = ScrollBarVisibility::Disabled;
    auto layout = ref new Grid();
    layout->Children->Append(panel);
    scroll->Content = layout;
    content->Content = scroll;
}

ref class ShellApp sealed : Application, Windows::UI::Xaml::Markup::IXamlMetadataProvider {
    Microsoft::UI::Xaml::XamlTypeInfo::XamlControlsXamlMetaDataProvider^ metadata_;
public:
    ShellApp() {
        try { metadata_ = ref new Microsoft::UI::Xaml::XamlTypeInfo::XamlControlsXamlMetaDataProvider(); }
        catch (Platform::Exception^) { metadata_ = nullptr; }
    }
    virtual Windows::UI::Xaml::Markup::IXamlType^ GetXamlType(Windows::UI::Xaml::Interop::TypeName type) {
        return metadata_ ? metadata_->GetXamlType(type) : nullptr;
    }
    virtual Windows::UI::Xaml::Markup::IXamlType^ GetXamlType(Platform::String^ name) {
        return metadata_ ? metadata_->GetXamlType(name) : nullptr;
    }
    virtual Platform::Array<Windows::UI::Xaml::Markup::XmlnsDefinition>^ GetXmlnsDefinitions() {
        return metadata_ ? metadata_->GetXmlnsDefinitions() : ref new Platform::Array<Windows::UI::Xaml::Markup::XmlnsDefinition>(0);
    }
protected:
    virtual void OnLaunched(LaunchActivatedEventArgs^) override {
        PROBE_STAGE(L"OnLaunched");
        auto window = Window::Current;
        dispatcher = window->Dispatcher;
        horizon_uwp_attach_shell(CoreWindow::GetForCurrentThread());
        page = ref new Page();
        content = ref new ContentControl();
        content->HorizontalContentAlignment = HorizontalAlignment::Stretch;
        content->VerticalContentAlignment = VerticalAlignment::Stretch;
        ui_settings = ref new UISettings();
        accessibility = ref new AccessibilitySettings();
        auto values = ApplicationData::Current->LocalSettings->Values;
        auto saved = values->HasKey(L"pregame-theme") ? values->Lookup(L"pregame-theme") : nullptr;
        int stored = 0;
        if (saved) { try { stored = safe_cast<IPropertyValue^>(saved)->GetInt32(); } catch (Platform::Exception^) {} }
        if (stored < 0 || stored > 3) stored = 0;
        selected_appearance = static_cast<AppAppearance>(stored);
        startup_appearance = selected_appearance;
        PROBE_STAGE(L"Before WinUI");
        try {
#ifdef HORIZON_UWP_SHELL_PLAIN_PROBE
            throw ref new Platform::FailureException();
#endif
            auto control_resources = ref new muxc::XamlControlsResources();
            control_resources->ControlsResourcesVersion = resolved_appearance(selected_appearance) == AppAppearance::Windows11SunValley
                ? muxc::ControlsResourcesVersion::Version2 : muxc::ControlsResourcesVersion::Version1;
            Resources->MergedDictionaries->Append(control_resources);
            navigation = ref new muxc::NavigationView();
            navigation->PaneDisplayMode = muxc::NavigationViewPaneDisplayMode::LeftCompact;
            navigation->IsBackButtonVisible = muxc::NavigationViewBackButtonVisible::Collapsed;
            navigation->IsBackEnabled = false;
            navigation->IsTitleBarAutoPaddingEnabled = true;
            navigation->Loaded += ref new RoutedEventHandler([](Platform::Object^, RoutedEventArgs^) { apply_theme(); });
            auto home = ref new muxc::NavigationViewItem();
            home->Content = L"Home"; home->Tag = L"home"; home->Icon = ref new SymbolIcon(Symbol::Home);
            navigation->MenuItems->Append(home);
            auto logs = ref new muxc::NavigationViewItem();
            logs->Content = L"Logs"; logs->Tag = L"logs"; logs->Icon = ref new SymbolIcon(Symbol::Document);
            navigation->MenuItems->Append(logs);
            auto news = ref new muxc::NavigationViewItem();
            news->Content = L"What's new"; news->Tag = L"news"; news->Icon = ref new SymbolIcon(Symbol::Globe);
            navigation->MenuItems->Append(news);
            profile_navigation = ref new muxc::NavigationViewItem();
            profile_navigation->Tag = L"profile";
            update_profile_navigation();
            navigation->FooterMenuItems->Append(profile_navigation);
            navigation->Content = content;
            navigation->SelectionChanged += ref new Windows::Foundation::TypedEventHandler<muxc::NavigationView^, muxc::NavigationViewSelectionChangedEventArgs^>(
                [](muxc::NavigationView^ sender, muxc::NavigationViewSelectionChangedEventArgs^ args) {
                    if (restoring_navigation) return;
                    if (prompt_kind != 0) return;
                    if (args->SelectedItem == profile_navigation) {
                        choose(2, displayed_generation);
                        if (last_navigation_selection) {
                            restoring_navigation = true;
                            sender->SelectedItem = last_navigation_selection;
                            restoring_navigation = false;
                        }
                        return;
                    }
                    last_navigation_selection = args->SelectedItem;
                    show_settings = args->IsSettingsSelected;
                    show_logs = false; show_news = false;
                    if (!show_settings) {
                        auto item = dynamic_cast<muxc::NavigationViewItem^>(args->SelectedItem);
                        auto tag = item ? dynamic_cast<Platform::String^>(item->Tag) : nullptr;
                        show_logs = tag && tag == L"logs";
                        show_news = tag && tag == L"news";
                    }
                    refresh();
                });
            navigation->SelectedItem = home;
            page->Content = navigation;
            has_winui = true;
            PROBE_STAGE(L"WinUI ready");
        } catch (Platform::Exception^) {
            PROBE_STAGE(L"WinUI fallback");
            // A damaged or unavailable WinUI framework must not prevent the
            // plain system XAML page from opening.
            auto split = ref new SplitView();
            split->DisplayMode = SplitViewDisplayMode::Inline;
            split->OpenPaneLength = 150;
            split->IsPaneOpen = true;
            auto pane = ref new StackPanel();
            auto home = ref new Button(); home->Content = L"Home";
            home->Click += ref new RoutedEventHandler([](Platform::Object^, RoutedEventArgs^) { if (prompt_kind == 0) { show_settings = false; show_logs = false; show_news = false; refresh(); } });
            auto logs = ref new Button(); logs->Content = L"Logs";
            logs->Click += ref new RoutedEventHandler([](Platform::Object^, RoutedEventArgs^) { if (prompt_kind == 0) { show_settings = false; show_logs = true; show_news = false; refresh(); } });
            auto news = ref new Button(); news->Content = L"What's new";
            news->Click += ref new RoutedEventHandler([](Platform::Object^, RoutedEventArgs^) { if (prompt_kind == 0) { show_settings = false; show_logs = false; show_news = true; refresh(); } });
            auto settings = ref new Button(); settings->Content = L"Settings";
            settings->Click += ref new RoutedEventHandler([](Platform::Object^, RoutedEventArgs^) { if (prompt_kind == 0) { show_settings = true; show_logs = false; show_news = false; refresh(); } });
            fallback_profile = ref new Button();
            fallback_profile->Click += ref new RoutedEventHandler([](Platform::Object^, RoutedEventArgs^) { if (prompt_kind == 0) choose(2, displayed_generation); });
            update_profile_navigation();
            pane->Children->Append(home); pane->Children->Append(logs); pane->Children->Append(news); pane->Children->Append(fallback_profile); pane->Children->Append(settings);
            split->Pane = pane; split->Content = content; page->Content = split;
        }
        apply_theme();
        PROBE_STAGE(L"Theme ready");
        window->Content = page;
        window->Activate();
        ui_settings->ColorValuesChanged += ref new TypedEventHandler<UISettings^, Platform::Object^>(
            [](UISettings^, Platform::Object^) {
                try { dispatcher->RunAsync(CoreDispatcherPriority::Normal, ref new DispatchedHandler([]() { apply_theme(); if (prompt_kind == 0) refresh(); })); }
                catch (...) {}
            });
        accessibility->HighContrastChanged += ref new TypedEventHandler<AccessibilitySettings^, Platform::Object^>(
            [](AccessibilitySettings^, Platform::Object^) {
                try { dispatcher->RunAsync(CoreDispatcherPriority::Normal, ref new DispatchedHandler([]() { apply_theme(); if (prompt_kind == 0) refresh(); })); }
                catch (...) {}
            });
        PROBE_STAGE(L"Window activated");
#ifdef HORIZON_UWP_NATIVE_D3D11
        renderer_known = true;
        renderer_available = probe_native_renderer(CoreWindow::GetForCurrentThread()) != 0;
        if (!renderer_available)
            show_message(L"Graphics compatibility", L"This device could not open Horizon's native Direct3D 11 feature level 9_1 renderer.");
#else
        renderer_known = true;
#ifdef HORIZON_UWP_DX12
        constexpr unsigned renderer_backend = 2;
#else
        constexpr unsigned renderer_backend = 1;
#endif
        auto coreWindow = CoreWindow::GetForCurrentThread();
        auto bounds = coreWindow->Bounds;
        const double scale = DisplayInformation::GetForCurrentView()->RawPixelsPerViewPixel;
        const auto width = static_cast<unsigned>(std::max<double>(1.0, std::ceil(bounds.Width * scale)));
        const auto height = static_cast<unsigned>(std::max<double>(1.0, std::ceil(bounds.Height * scale)));
        renderer_available = horizon_uwp_probe_renderer(
            reinterpret_cast<void*>(reinterpret_cast<IUnknown*>(coreWindow)), renderer_backend, width, height) != 0;
#endif
        if (prompt_kind == 0) refresh();
        ThreadPool::RunAsync(ref new WorkItemHandler([](IAsyncAction^) {
            PROBE_STAGE(L"Worker started");
            auto local = ApplicationData::Current->LocalFolder->Path;
            auto game = Platform::String::Concat(local, L"\\game");
            const int result = horizon_uwp_prepare(reinterpret_cast<const uint16_t*>(game->Data()), game->Length());
            if (result == 1) dispatcher->RunAsync(CoreDispatcherPriority::Normal,
                ref new DispatchedHandler([]() { horizon_uwp_open_game_view(); }));
        }));
    }
};
}

extern "C" int __cdecl horizon_uwp_dialog_show(const unsigned char* bytes, size_t length, unsigned kind) {
    try {
        auto value = decode(bytes, length);
        if (!dispatcher || !value || kind > 7) return -1;
        auto accounts = kind == 5 ? account_labels : nullptr;
        const unsigned generation = ++prompt_generation;
        choice.store(0);
        dispatcher->RunAsync(CoreDispatcherPriority::Normal, ref new DispatchedHandler([value, accounts, kind, generation]() {
            if (generation != prompt_generation.load()) return;
            try {
                displayed_generation = generation; prompt_text = value; prompt_kind = kind;
                if (kind == 0) refresh(); else show_prompt_dialog(value, accounts, kind, generation);
                PROBE_STAGE(L"Prompt ready");
            }
            catch (...) { choice.store(-2); }
        }));
        return 0;
    } catch (...) { return -1; }
}
extern "C" int __cdecl horizon_uwp_dialog_poll() { return choice.load(); }
extern "C" void __cdecl horizon_uwp_dialog_cancel() {
    try {
        const auto generation = prompt_generation.load();
        if (!dispatcher) { choice.store(-1); return; }
        dispatcher->RunAsync(CoreDispatcherPriority::Normal, ref new DispatchedHandler([generation]() {
            if (generation != prompt_generation.load()) return;
            if (active_dialog) { dialog_result = -1; active_dialog->Hide(); }
            else choose(-1, generation);
        }));
    } catch (...) { choice.store(-1); }
}
extern "C" void __cdecl horizon_uwp_page_profile(const unsigned char* bytes, size_t length, unsigned kind) {
    try {
        auto value = length ? decode(bytes, length) : ref new Platform::String(L"Profile");
        const unsigned current_kind = length && kind <= 3 ? kind : 0;
        if (value && dispatcher) dispatcher->RunAsync(CoreDispatcherPriority::Normal,
            ref new DispatchedHandler([value, current_kind]() { profile = value; profile_kind = current_kind; update_profile_navigation(); if (prompt_kind == 0) refresh(); }));
    } catch (...) {}
}
extern "C" void __cdecl horizon_uwp_page_skin(const unsigned char* bytes, size_t length) {
    try {
        const auto generation = ++skin_generation;
        auto value = length ? decode(bytes, length) : nullptr;
        if (dispatcher) dispatcher->RunAsync(CoreDispatcherPriority::Normal,
            ref new DispatchedHandler([value, generation]() {
                if (generation != skin_generation.load()) return;
                profile_skin = nullptr;
                update_profile_navigation();
                if (prompt_kind == 0) refresh();
                if (value) load_profile_skin(value, generation);
            }));
    } catch (...) {}
}
extern "C" void __cdecl horizon_uwp_page_accounts(const unsigned char* bytes, size_t length) {
    try { account_labels = decode(bytes, length); } catch (...) { account_labels = nullptr; }
}
extern "C" size_t __cdecl horizon_uwp_dialog_text(unsigned char* buffer, size_t capacity) {
    const size_t length = offline_input.size();
    if (buffer && length <= capacity) memcpy(buffer, offline_input.data(), length);
    return length;
}
extern "C" void __cdecl horizon_uwp_page_tick() {}
extern "C" void __cdecl horizon_uwp_page_event(const void*) {}
extern "C" void __cdecl horizon_uwp_page_shutdown() {}
extern "C" int __cdecl horizon_uwp_xaml_start() {
    Application::Start(ref new ApplicationInitializationCallback([](ApplicationInitializationCallbackParams^) { ref new ShellApp(); }));
    return 0;
}
