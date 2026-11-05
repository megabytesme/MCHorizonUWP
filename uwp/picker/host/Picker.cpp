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
#include <string>
#include <vector>

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

task<void> copy_folder(StorageFolder^ source, StorageFolder^ destination) {
    return create_task(source->GetItemsAsync()).then([destination](IVectorView<IStorageItem^>^ items) {
        std::vector<task<void>> copies;
        for (unsigned int i = 0; i < items->Size; ++i) {
            auto item = items->GetAt(i);
            auto file = dynamic_cast<StorageFile^>(item);
            if (file) {
                // A live Java world lock is process-specific and must never be
                // carried into an imported or exported copy.
                if (file->Name == L"session.lock") continue;
                copies.push_back(create_task(file->CopyAsync(destination, file->Name, NameCollisionOption::FailIfExists))
                    .then([](StorageFile^) {}));
                continue;
            }
            auto folder = dynamic_cast<StorageFolder^>(item);
            if (folder) {
                copies.push_back(create_task(destination->CreateFolderAsync(folder->Name, CreationCollisionOption::FailIfExists))
                    .then([folder](StorageFolder^ target) { return copy_folder(folder, target); }));
            }
        }
        return when_all(copies.begin(), copies.end());
    });
}

void finish_folder_copy(task<void> pending) {
    try {
        pending.get();
        active.store(false);
        notice(L"Save copied. Your original and existing saves were left unchanged.");
    } catch (...) {
        active.store(false);
        notice(L"The save could not be copied. Your original save was left unchanged.");
    }
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

// Save folders are copied through the brokered folder picker. Imports require
// level.dat, exports and imports use unique destination names, and neither
// operation moves or overwrites a user's save.
extern "C" int __cdecl horizon_uwp_pick_save_folder(const wchar_t* path, size_t length, unsigned int kind) {
    if (!path || !length || length > 32767 || (kind != 1 && kind != 2)) return -1;
    bool expected = false;
    if (!active.compare_exchange_strong(expected, true)) return -1;
    try {
        auto local_path = ref new Platform::String(path, static_cast<unsigned int>(length));
        auto picker = ref new FolderPicker();
        picker->SuggestedStartLocation = PickerLocationId::Downloads;
        picker->FileTypeFilter->Append(L"*");
        auto context = task_continuation_context::use_current();
        create_task(picker->PickSingleFolderAsync()).then([local_path, kind, context](task<StorageFolder^> picked) {
            StorageFolder^ source = nullptr;
            try { source = picked.get(); }
            catch (...) { active.store(false); notice(L"The save folder picker could not be opened."); return; }
            if (!source) { active.store(false); return; }
            if (kind == 1) {
                create_task(source->GetFileAsync(L"level.dat")).then([source, local_path, context](task<StorageFile^> level) {
                    try { level.get(); }
                    catch (...) { active.store(false); notice(L"Choose a Minecraft world folder containing level.dat."); return; }
                    create_task(StorageFolder::GetFolderFromPathAsync(local_path)).then([source, context](task<StorageFolder^> target_task) {
                        try {
                            auto target = target_task.get();
                            create_task(target->CreateFolderAsync(source->Name, CreationCollisionOption::GenerateUniqueName)).then([source](StorageFolder^ save) {
                                finish_folder_copy(copy_folder(source, save));
                            }, context);
                        } catch (...) { active.store(false); notice(L"The app's saves folder could not be opened."); }
                    }, context);
                }, context);
            } else {
                auto destination = source;
                create_task(StorageFolder::GetFolderFromPathAsync(local_path)).then([destination, context](task<StorageFolder^> source_task) {
                    try {
                        auto save_source = source_task.get();
                        create_task(destination->CreateFolderAsync(save_source->Name, CreationCollisionOption::GenerateUniqueName)).then([save_source](StorageFolder^ save_copy) {
                            finish_folder_copy(copy_folder(save_source, save_copy));
                        }, context);
                    } catch (...) { active.store(false); notice(L"The selected destination folder could not be opened."); }
                }, context);
            }
        }, context);
        return 0;
    } catch (...) {
        active.store(false);
        return -1;
    }
}
