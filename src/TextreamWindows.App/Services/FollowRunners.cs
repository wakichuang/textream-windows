using System.Diagnostics;
using TextreamWindows.Core.Session;
using TextreamWindows.Core.Text;
using TextreamWindows.Speech.Audio;
using TextreamWindows.Speech.Follow;
using TextreamWindows.Speech.Models;
using TextreamWindows.Speech.Recognition;

namespace TextreamWindows.App.Services;

/// <summary>三種模式怎麼跑：定速捲動只要計時器；有講話才捲要麥克風、不要模型；逐字追蹤要麥克風加模型 A（fp32，第 1 節）。</summary>
public static class FollowRunners
{
    /// <summary>從講稿位置 <paramref name="startAt"/> 開始（瓦基在編輯器放的游標、或上次停下的地方）。</summary>
    public static async Task<IFollowRunner> StartAsync(PromptScript prompt, FollowMode mode, double scrollSpeed, MicrophoneInfo? microphone, int startAt)
    {
        var session = new PromptSession(prompt, mode, scrollSpeed);
        session.JumpTo(startAt, 0); // 還沒開始時跳：Start 會從這裡重新起算
        if (mode == FollowMode.Classic)
        {
            return new TimerRunner(session);
        }
        if (microphone is null)
        {
            throw new InvalidOperationException("找不到麥克風。插上麥克風後重新開啟 Textream for Windows。");
        }

        ISpeechEngine engine = mode == FollowMode.WordTracking
            ? await Task.Run(() => new SpeechEngine(SpeechModel.Resolve("A", preferFp32: true))) // 載入模型約 1～2 秒，不卡畫面
            : new NullSpeechEngine();
        var source = new MicrophoneCapture(microphone);
        return new PipelineRunner(new FollowPipeline(source, engine, session), source, engine);
    }

    /// <summary>定速捲動：只有計時器，時間用 Stopwatch。</summary>
    private sealed class TimerRunner : IFollowRunner
    {
        private readonly PromptSession _session;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private bool _stopped;

        public TimerRunner(PromptSession session)
        {
            _session = session;
            _session.Start(0);
        }

        public int Progress => _session.EffectiveCharacterCount;

        public bool IsSpeaking => false;

        public float AudioLevel => 0;

        public bool HasStopped => _stopped || _session.IsFinished;

        public Exception? Error => null;

        public bool IsPaused { get; private set; }

        public string Heard => "";

        public void Tick() => _session.Tick(_clock.Elapsed.TotalSeconds);

        public void Pause()
        {
            _session.Pause();
            IsPaused = true;
        }

        public void Resume()
        {
            if (IsPaused)
            {
                _session.Resume(_clock.Elapsed.TotalSeconds);
                IsPaused = false;
            }
        }

        public void JumpTo(int characterOffset) => _session.JumpTo(characterOffset, _clock.Elapsed.TotalSeconds);

        public void Stop()
        {
            _session.Pause();
            _stopped = true;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>麥克風 → 引擎 → session。讀完就自己把麥克風關掉。</summary>
    private sealed class PipelineRunner : IFollowRunner
    {
        private readonly FollowPipeline _pipeline;
        private readonly IAudioSource _source;
        private readonly ISpeechEngine _engine;
        private volatile bool _hasStopped;
        private Exception? _error;

        public PipelineRunner(FollowPipeline pipeline, IAudioSource source, ISpeechEngine engine)
        {
            _pipeline = pipeline;
            _source = source;
            _engine = engine;
            _pipeline.Stopped += error =>
            {
                _error = error;
                _hasStopped = true;
            };
            _pipeline.Start();
        }

        public int Progress => _pipeline.EffectiveCharacterCount;

        public bool IsSpeaking => _pipeline.IsSpeaking;

        public float AudioLevel => _pipeline.AudioLevel;

        public bool HasStopped => _hasStopped;

        public Exception? Error => _error;

        public bool IsPaused => _pipeline.IsPaused;

        public string Heard => _pipeline.Heard;

        public void Pause() => _pipeline.Pause();

        public void Resume() => _pipeline.Resume();

        public void JumpTo(int characterOffset) => _pipeline.JumpTo(characterOffset);

        public void Tick()
        {
            _pipeline.Tick();
            if (Progress >= _pipeline.Session.Prompt.CharacterCount)
            {
                _pipeline.Stop();
            }
        }

        public void Stop() => _pipeline.Stop();

        public void Dispose()
        {
            _pipeline.Dispose();
            _source.Dispose();
            _engine.Dispose();
        }
    }
}
