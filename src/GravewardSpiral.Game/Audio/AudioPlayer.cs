using Microsoft.Xna.Framework.Audio;
using NLayer;

namespace GravewardSpiral.Game.Audio;

/// <summary>
/// BGM と効果音を鳴らす。
/// BGM は MP3 を少しずつ読んで流す(ループ、フェードイン・フェードアウト)。
/// 効果音は Content/&lt;パック&gt;/audio/se/&lt;id&gt;.wav か .mp3 を初めて鳴らすときに読む。ファイルが無い音は鳴らさない(素材が届いた順に入れればよい)。
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private const int SamplesPerBuffer = 4410;   // 1回に渡す量(チャンネルあたり 0.1 秒)
    private const int BuffersAhead = 3;

    private readonly Dictionary<string, SoundEffect?> _effects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, string?> _resolve;
    private Bgm? _bgm;
    private Bgm? _fadingOut;

    /// <param name="resolve">Content からの相対パスの候補を受け取り、あれば実際のパスを返す。</param>
    public AudioPlayer(Func<string, string?> resolve, bool enabled)
    {
        _resolve = resolve;
        Enabled = enabled;
    }

    /// <summary>false なら何も鳴らさない(確認用の起動など)。</summary>
    public bool Enabled { get; }

    public float BgmVolume { get; set; } = 0.7f;
    public float SeVolume { get; set; } = 0.8f;

    public string? CurrentBgm => _bgm?.Path;
    public float CurrentBgmGain => _bgm?.Gain ?? 1f;

    /// <summary>次に鳴らす BGM を、この秒数から始める(1回だけ。確認用)。</summary>
    public double? NextBgmStart { get; set; }

    /// <summary>BGM として渡した量(0.1 秒ずつ)と、頭に戻った回数(確認用)。</summary>
    public int BgmBuffersSubmitted { get; private set; }
    public int BgmLoops { get; private set; }

    /// <summary>その曲のファイルがあるか(パックのどれかに)。</summary>
    public bool Has(string path) => _resolve(path) != null;

    /// <summary>BGM を鳴らす。同じ曲が鳴っていれば何もしない。path は Content からの相対パス。gain はその曲だけの音量の倍率(小さく流したい曲)。</summary>
    public void PlayBgm(string path, float fadeInSeconds = 1.5f, float gain = 1f)
    {
        if (!Enabled || _bgm?.Path == path) return;
        StopBgm();
        string? full = _resolve(path);
        if (full == null)
        {
            Console.Error.WriteLine($"BGM がありません: {path}");
            return;
        }
        try
        {
            _bgm = new Bgm(path, full, fadeInSeconds, this, NextBgmStart) { Gain = gain };
            NextBgmStart = null;
            Console.WriteLine($"bgm: {path} ({_bgm.SampleRate} Hz, {_bgm.Channels} ch)");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or NoAudioHardwareException)
        {
            Console.Error.WriteLine($"BGM を鳴らせません: {path}: {ex.Message}");
            _bgm = null;
            // 音の装置がまだ空いていない(前のゲームを止めた直後など)ことがあるので、少し待って試し直す
            if (_retries < MaxRetries) _retry = (path, fadeInSeconds, gain, RetrySeconds);
        }
    }

    private const int MaxRetries = 5;
    private const double RetrySeconds = 1.0;
    private (string Path, float Fade, float Gain, double Wait)? _retry;
    private int _retries;

    public void StopBgm(float fadeOutSeconds = 1.0f)
    {
        _retry = null;
        if (_bgm == null) return;
        _fadingOut?.Dispose();
        _fadingOut = _bgm;
        _fadingOut.FadeOut(fadeOutSeconds);
        _bgm = null;
    }

    /// <summary>効果音を鳴らす。id は audio/se/ の下のファイル名(拡張子なし)。</summary>
    public void PlaySe(string id)
    {
        if (!Enabled) return;
        if (!_effects.TryGetValue(id, out var effect))
        {
            effect = LoadEffect(id);
            _effects[id] = effect;
        }
        effect?.Play(SeVolume, 0f, 0f);
    }

    private SoundEffect? LoadEffect(string id)
    {
        foreach (var ext in new[] { ".wav", ".mp3" })
        {
            string? full = _resolve($"audio/se/{id}{ext}");
            if (full == null) continue;
            try
            {
                if (ext == ".wav")
                {
                    using var stream = File.OpenRead(full);
                    return SoundEffect.FromStream(stream);
                }
                using var mp3 = new MpegFile(full);
                var floats = new List<float>();
                var chunk = new float[SamplesPerBuffer * mp3.Channels];
                int read;
                while ((read = mp3.ReadSamples(chunk, 0, chunk.Length)) > 0) floats.AddRange(chunk.AsSpan(0, read));
                return new SoundEffect(ToPcm16(floats.ToArray(), floats.Count), mp3.SampleRate, mp3.Channels == 2 ? AudioChannels.Stereo : AudioChannels.Mono);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
            {
                Console.Error.WriteLine($"効果音が読めません: {full}: {ex.Message}");
            }
        }
        return null;   // 素材がまだ無い
    }

    /// <summary>毎フレーム呼ぶ。BGM の続きを渡し、フェードを進める。</summary>
    public void Update(double dt)
    {
        if (_retry is { } r)
        {
            if (r.Wait - dt > 0) _retry = r with { Wait = r.Wait - dt };
            else
            {
                _retry = null;
                _retries++;
                PlayBgm(r.Path, r.Fade, r.Gain);
                if (_bgm != null) _retries = 0;
            }
        }
        // 鳴っているはずの BGM が止まっていたら(音の装置が入れ替わったなど)、頭から鳴らし直す
        if (_bgm is { Stalled: true } stalled)
        {
            Console.Error.WriteLine($"BGM が止まっていたので鳴らし直します: {stalled.Path}");
            string path = stalled.Path;
            float gain = stalled.Gain;
            stalled.Dispose();
            _bgm = null;
            PlayBgm(path, 0.5f, gain);
        }
        _bgm?.Update(dt, BgmVolume);
        if (_fadingOut != null && !_fadingOut.Update(dt, BgmVolume))
        {
            _fadingOut.Dispose();
            _fadingOut = null;
        }
    }

    internal static byte[] ToPcm16(float[] samples, int count)
    {
        var bytes = new byte[count * 2];
        for (int i = 0; i < count; i++)
        {
            short v = (short)Math.Clamp((int)(samples[i] * 32767f), short.MinValue, short.MaxValue);
            bytes[2 * i] = (byte)v;
            bytes[2 * i + 1] = (byte)(v >> 8);
        }
        return bytes;
    }

    public void Dispose()
    {
        _bgm?.Dispose();
        _fadingOut?.Dispose();
        foreach (var e in _effects.Values) e?.Dispose();
        _effects.Clear();
    }

    /// <summary>流している BGM 1曲。終わりまで読んだら頭から読み直してループする。</summary>
    private sealed class Bgm : IDisposable
    {
        private readonly string _file;
        private readonly AudioPlayer _owner;
        private readonly DynamicSoundEffectInstance _instance;
        private readonly float[] _floats;
        private MpegFile _mp3;
        private float _fade;
        private float _fadeSpeed;

        public Bgm(string path, string file, float fadeInSeconds, AudioPlayer owner, double? startSeconds)
        {
            Path = path;
            _owner = owner;
            _file = file;
            _mp3 = new MpegFile(file);
            if (startSeconds is double start && _mp3.CanSeek) _mp3.Time = TimeSpan.FromSeconds(start);
            SampleRate = _mp3.SampleRate;
            Channels = _mp3.Channels;
            _floats = new float[SamplesPerBuffer * Channels];
            _instance = new DynamicSoundEffectInstance(SampleRate, Channels == 2 ? AudioChannels.Stereo : AudioChannels.Mono);
            _fade = fadeInSeconds > 0 ? 0f : 1f;
            _fadeSpeed = fadeInSeconds > 0 ? 1f / fadeInSeconds : 0f;
            _instance.Volume = 0f;
            Fill();
            _instance.Play();
        }

        public string Path { get; }
        public float Gain { get; init; } = 1f;
        public int SampleRate { get; }
        public int Channels { get; }

        public void FadeOut(float seconds) => _fadeSpeed = seconds > 0 ? -1f / seconds : -1000f;

        /// <summary>鳴らし続けるなら true。フェードアウトし終えたら false。</summary>
        /// <summary>フェードアウトしていないのに、止まっている(音の装置を失ったときなど)。</summary>
        public bool Stalled => _fadeSpeed >= 0 && _instance.State == SoundState.Stopped;

        public bool Update(double dt, float volume)
        {
            _fade = Math.Clamp(_fade + _fadeSpeed * (float)dt, 0f, 1f);
            _instance.Volume = volume * _fade * Gain;
            if (_fadeSpeed < 0 && _fade <= 0f)
            {
                _instance.Stop();
                return false;
            }
            Fill();
            return true;
        }

        private void Fill()
        {
            while (_instance.PendingBufferCount < BuffersAhead)
            {
                int read = _mp3.ReadSamples(_floats, 0, _floats.Length);
                if (read <= 0)
                {
                    // 曲の終わり: 頭から読み直す
                    _mp3.Dispose();
                    _mp3 = new MpegFile(_file);
                    _owner.BgmLoops++;
                    read = _mp3.ReadSamples(_floats, 0, _floats.Length);
                    if (read <= 0) return;
                }
                _instance.SubmitBuffer(ToPcm16(_floats, read));
                _owner.BgmBuffersSubmitted++;
            }
        }

        public void Dispose()
        {
            _instance.Dispose();
            _mp3.Dispose();
        }
    }
}
