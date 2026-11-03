#include <windows.h>
#include <windows.foundation.h>
#include <windows.ui.popups.h>
#include <atomic>

using namespace Windows::Foundation;
using namespace Windows::UI::Popups;
namespace {
std::atomic<int> choice{ -1 };
IAsyncOperation<IUICommand^>^ active = nullptr;
void add(MessageDialog^ dialog, const wchar_t* label, int id) {
    dialog->Commands->Append(ref new UICommand(ref new Platform::String(label), nullptr,
                                               PropertyValue::CreateInt32(id)));
}
}

extern "C" int __cdecl horizon_uwp_dialog_show(const unsigned char* text, size_t length, unsigned int kind) {
    if (!text || !length || length > 4096 || (active != nullptr && choice.load() == 0)) return -1;
    try {
        active = nullptr;
        const int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
            reinterpret_cast<const char*>(text), static_cast<int>(length), nullptr, 0);
        if (count <= 0) return -1;
        wchar_t wide[4097] = {};
        if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
            reinterpret_cast<const char*>(text), static_cast<int>(length), wide, count) != count) return -1;
        auto dialog = ref new MessageDialog(ref new Platform::String(wide, count), L"Horizon");
        switch (kind) {
        case 0: add(dialog, L"Play offline", 1); add(dialog, L"Account", 2); break;
        case 1: add(dialog, L"Sign in", 1); add(dialog, L"Saved account", 2); break;
        case 2: add(dialog, L"Cancel", 1); break;
        case 3: add(dialog, L"OK", 1); break;
        case 4: add(dialog, L"Remove account", 1); add(dialog, L"Cancel", 2); break;
        case 5: add(dialog, L"Use saved account", 1); add(dialog, L"Sign out", 2); break;
        case 6: add(dialog, L"Continue", 1); add(dialog, L"Cancel", 2); break;
        default: return -1;
        }
        if (kind == 4 || kind == 6) {
            dialog->DefaultCommandIndex = 1;
            dialog->CancelCommandIndex = 1;
        }
        choice.store(0);
        active = dialog->ShowAsync();
        active->Completed = ref new AsyncOperationCompletedHandler<IUICommand^>(
            [](IAsyncOperation<IUICommand^>^ op, Windows::Foundation::AsyncStatus status) {
                int value = -1;
                try {
                    if (status == Windows::Foundation::AsyncStatus::Completed) {
                        auto command = op->GetResults();
                        if (command != nullptr && command->Id != nullptr)
                            value = safe_cast<IPropertyValue^>(command->Id)->GetInt32();
                    }
                } catch (...) {}
                choice.store(value);
            });
        return 0;
    } catch (...) { choice.store(-1); active = nullptr; return -1; }
}
extern "C" int __cdecl horizon_uwp_dialog_poll() { return choice.load(); }
extern "C" void __cdecl horizon_uwp_dialog_cancel() {
    try { if (active != nullptr && choice.load() == 0) safe_cast<Windows::Foundation::IAsyncInfo^>(active)->Cancel(); }
    catch (...) { choice.store(-1); }
}
