// UWP content importer: picked files are copied, never moved or overwritten.
#if defined(_MSC_VER) && _MSC_VER >= 1940 && defined(WINAPI_FAMILY)
extern "C" __declspec(dllimport) char* __cdecl getenv(const char*);
extern "C" __declspec(dllimport) int __cdecl system(const char*);
#endif
#include <windows.h>
#include <ppltasks.h>
#include <windows.storage.h>
#include <windows.storage.pickers.h>
#include <windows.ui.popups.h>
#include <atomic>
#include <memory>

using namespace Concurrency;
using namespace Windows::Foundation;
using namespace Windows::Foundation::Collections;
using namespace Windows::Storage;
using namespace Windows::Storage::Pickers;
using namespace Windows::UI::Popups;
namespace {
std::atomic<bool> active{false};
void notice(const wchar_t* message) {
    try { (ref new MessageDialog(ref new Platform::String(message), L"Horizon"))->ShowAsync(); }
    catch (...) {}
}
}

extern "C" int __cdecl horizon_uwp_pick_files(const wchar_t* path, size_t length, unsigned int kind) {
    if (!path || !length || length > 32767 || (kind != 1 && kind != 2)) return -1;
    bool expected = false;
    if (!active.compare_exchange_strong(expected, true)) return -1;
    try {
        auto destination = ref new Platform::String(path, static_cast<unsigned int>(length));
        auto picker = ref new FileOpenPicker();
        picker->ViewMode = PickerViewMode::List;
        picker->SuggestedStartLocation = PickerLocationId::Downloads;
        picker->FileTypeFilter->Append(kind == 1 ? L".hmod" : L".zip");
        auto context = task_continuation_context::use_current();
        create_task(picker->PickMultipleFilesAsync()).then(
            [destination, context](task<IVectorView<StorageFile^>^> picked) {
                try {
                    auto files = picked.get();
                    if (!files || !files->Size) { active.store(false); return; }
                    create_task(StorageFolder::GetFolderFromPathAsync(destination)).then(
                        [files, context](task<StorageFolder^> folder_task) {
                            try {
                                auto folder = folder_task.get();
                                auto pending = std::make_shared<std::atomic<unsigned int>>(files->Size);
                                auto failed = std::make_shared<std::atomic<bool>>(false);
                                for (unsigned int i = 0; i < files->Size; ++i) {
                                    try {
                                        auto file = files->GetAt(i);
                                        create_task(file->CopyAsync(folder, file->Name, NameCollisionOption::GenerateUniqueName))
                                            .then([pending, failed](task<StorageFile^> copy) {
                                                try { copy.get(); } catch (...) { failed->store(true); }
                                                if (pending->fetch_sub(1) == 1) {
                                                    active.store(false);
                                                    notice(failed->load()
                                                        ? L"Some files could not be imported. Existing files were preserved."
                                                        : L"Files imported. Close and reopen this screen to refresh the list.");
                                                }
                                            }, context);
                                    } catch (...) {
                                        failed->store(true);
                                        if (pending->fetch_sub(1) == 1) {
                                            active.store(false);
                                            notice(L"Some files could not be imported. Existing files were preserved.");
                                        }
                                    }
                                }
                            } catch (...) {
                                active.store(false);
                                notice(L"The destination folder could not be opened.");
                            }
                        }, context);
                } catch (...) {
                    active.store(false);
                    notice(L"The file picker could not be opened.");
                }
            }, context);
        return 0;
    } catch (...) {
        active.store(false);
        return -1;
    }
}
