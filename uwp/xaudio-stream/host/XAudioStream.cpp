// UWP XAudio2 output for Horizon's existing stereo CPU mixer.
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <windows.h>
#include <wrl/client.h>
#include <xaudio2.h>
using Fill = void(__cdecl *)(void *, float *, size_t);
namespace {
constexpr unsigned kChannels = 2, kFrames = 1024, kBuffers = 3, kRate = 48000;
class Output final : public IXAudio2VoiceCallback {
  Microsoft::WRL::ComPtr<IXAudio2> engine_;
  IXAudio2MasteringVoice *master_ = nullptr;
  IXAudio2SourceVoice *voice_ = nullptr;
  Fill fill_;
  void *context_;
  std::atomic<bool> closing_{false};
  float samples_[kBuffers][kFrames * kChannels]{};
  HRESULT enqueue(unsigned slot) {
    fill_(context_, samples_[slot], kFrames * kChannels);
    XAUDIO2_BUFFER b{};
    b.AudioBytes = sizeof(samples_[slot]);
    b.pAudioData = reinterpret_cast<const BYTE *>(samples_[slot]);
    b.pContext = reinterpret_cast<void *>(static_cast<uintptr_t>(slot + 1));
    return voice_->SubmitSourceBuffer(&b);
  }

public:
  Output(Fill f, void *c) : fill_(f), context_(c) {}
  ~Output() {
    closing_.store(true);
    if (voice_) {
      voice_->Stop();
      voice_->DestroyVoice();
    }
    if (master_)
      master_->DestroyVoice();
  }
  bool start() {
    HRESULT hr =
        XAudio2Create(engine_.GetAddressOf(), 0, XAUDIO2_DEFAULT_PROCESSOR);
    if (SUCCEEDED(hr))
      hr = engine_->CreateMasteringVoice(&master_);
    WAVEFORMATEX f{};
    f.wFormatTag = WAVE_FORMAT_IEEE_FLOAT;
    f.nChannels = kChannels;
    f.nSamplesPerSec = kRate;
    f.wBitsPerSample = 32;
    f.nBlockAlign = kChannels * sizeof(float);
    f.nAvgBytesPerSec = kRate * f.nBlockAlign;
    if (SUCCEEDED(hr))
      hr = engine_->CreateSourceVoice(&voice_, &f, 0,
                                      XAUDIO2_DEFAULT_FREQ_RATIO, this);
    for (unsigned i = 0; i < kBuffers && SUCCEEDED(hr); i++)
      hr = enqueue(i);
    if (SUCCEEDED(hr))
      hr = voice_->Start();
    return SUCCEEDED(hr);
  }
  void STDMETHODCALLTYPE OnVoiceProcessingPassStart(UINT32) override {}
  void STDMETHODCALLTYPE OnVoiceProcessingPassEnd() override {}
  void STDMETHODCALLTYPE OnStreamEnd() override {}
  void STDMETHODCALLTYPE OnBufferStart(void *) override {}
  void STDMETHODCALLTYPE OnLoopEnd(void *) override {}
  void STDMETHODCALLTYPE OnVoiceError(void *, HRESULT) override {
    closing_.store(true);
  }
  void STDMETHODCALLTYPE OnBufferEnd(void *token) override {
    uintptr_t n = reinterpret_cast<uintptr_t>(token);
    if (!closing_.load() && n >= 1 && n <= kBuffers) {
      if (FAILED(enqueue(static_cast<unsigned>(n - 1))))
        closing_.store(true);
    }
  }
};
} // namespace
extern "C" void *__cdecl horizon_xaudio_open(Fill callback, void *context) {
  if (!callback || !context)
    return nullptr;
  try {
    Output *output = new Output(callback, context);
    if (!output->start()) {
      delete output;
      return nullptr;
    }
    return output;
  } catch (...) {
    return nullptr;
  }
}
extern "C" void __cdecl horizon_xaudio_close(void *output) {
  delete static_cast<Output *>(output);
}
