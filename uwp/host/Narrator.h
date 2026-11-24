#pragma once

#include <windows.h>
#include <windows.media.core.h>
#include <windows.media.playback.h>
#include <windows.media.speechsynthesis.h>
#include <windows.storage.h>
#include <atomic>
#include <memory>

namespace horizon_uwp::narrator {
using namespace Windows::Foundation;
using namespace Windows::Media::Playback;
using namespace Windows::Media::SpeechSynthesis;

constexpr wchar_t voice_key[] = L"narrator-voice-id";

inline Platform::String^ saved_voice() {
    auto values = Windows::Storage::ApplicationData::Current->LocalSettings->Values;
    try {
        auto value = dynamic_cast<IPropertyValue^>(values->Lookup(ref new Platform::String(voice_key)));
        if (value && value->Type == PropertyType::String) return value->GetString();
    } catch (Platform::OutOfBoundsException^) {}
    return L"";
}

inline VoiceInformation^ find_voice(
    Windows::Foundation::Collections::IVectorView<VoiceInformation^>^ voices,
    Platform::String^ id) {
    if (!id || id->IsEmpty()) return nullptr;
    for (unsigned i = 0; i < voices->Size; ++i) {
        auto voice = voices->GetAt(i);
        if (voice->Id == id) return voice;
    }
    return nullptr;
}

inline void save_voice(Platform::String^ id) {
    if (id && !id->IsEmpty() && !find_voice(SpeechSynthesizer::AllVoices, id))
        throw ref new Platform::InvalidArgumentException();
    Windows::Storage::ApplicationData::Current->LocalSettings->Values->Insert(
        ref new Platform::String(voice_key), PropertyValue::CreateString(id));
}

// Owned by one apartment. Only completion flags are accessed by MediaPlayer
// callbacks, and each utterance owns its own flag and player.
class Speech {
public:
    Speech() : synth_(ref new SpeechSynthesizer()), player_(make_player()) {
        if (!synth_->Voice) throw Platform::Exception::CreateException(E_FAIL);
    }
    ~Speech() { clear(); }

    void start(Platform::String^ text, double volume) {
        if (pending_ || completion_) clear();
        if (!player_) player_ = make_player();
        const auto id = saved_voice();
        auto voice = id && !id->IsEmpty() ? find_voice(SpeechSynthesizer::AllVoices, id) : nullptr;
        if (!voice) voice = SpeechSynthesizer::DefaultVoice;
        if (voice && synth_->Voice->Id != voice->Id) synth_->Voice = voice;
        player_->Volume = volume;
        pending_ = synth_->SynthesizeTextToStreamAsync(text);
    }

    // S_FALSE while synthesizing or playing, S_OK when idle, or a failure HRESULT.
    HRESULT poll() {
        try {
            if (pending_) {
                if (pending_->Status == Windows::Foundation::AsyncStatus::Started) return S_FALSE;
                auto stream = pending_->GetResults();
                pending_ = nullptr;
                auto source = Windows::Media::Core::MediaSource::CreateFromStream(
                    stream, stream->ContentType);
                auto completion = std::make_shared<std::atomic<HRESULT>>(S_FALSE);
                player_->MediaEnded += ref new TypedEventHandler<MediaPlayer^, Platform::Object^>(
                    [completion](MediaPlayer^, Platform::Object^) { completion->store(S_OK); });
                player_->MediaFailed += ref new TypedEventHandler<MediaPlayer^, MediaPlayerFailedEventArgs^>(
                    [completion](MediaPlayer^, MediaPlayerFailedEventArgs^ args) {
                        auto error = args->ExtendedErrorCode.Value;
                        completion->store(FAILED(error) ? error : E_FAIL);
                    });
                completion_ = completion;
                player_->Source = source;
                player_->Play();
            }
            if (!completion_) return S_OK;
            auto result = completion_->load();
            if (result != S_FALSE) clear();
            return result;
        } catch (Platform::Exception^ error) {
            clear();
            return error->HResult;
        }
    }

    void clear() noexcept {
        if (pending_) {
            try { pending_->Cancel(); } catch (Platform::Exception^) {}
            pending_ = nullptr;
        }
        if (player_) {
            try { delete player_; } catch (Platform::Exception^) {}
            player_ = nullptr;
        }
        completion_.reset();
    }

private:
    static MediaPlayer^ make_player() {
        auto player = ref new MediaPlayer();
        player->CommandManager->IsEnabled = false;
        return player;
    }

    SpeechSynthesizer^ synth_;
    MediaPlayer^ player_;
    IAsyncOperation<SpeechSynthesisStream^>^ pending_;
    std::shared_ptr<std::atomic<HRESULT>> completion_;
};
}
