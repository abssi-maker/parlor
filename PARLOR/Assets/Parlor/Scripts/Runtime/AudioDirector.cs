using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Parlor
{
    /// <summary>
    /// All sound. Every source is spatial and placed where the thing is: the music box plays from the pit,
    /// the voice from the magician's head, the coin from the coin. Music ducks under voice lines.
    /// Sound effects and music are synthesised by tools/synth_audio.py; the magician's lines are pre-generated
    /// audio files from tools/tts_lines.py. Nothing is generated at runtime.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        const float MusicVolume = 0.30f, DuckedVolume = 0.10f;

        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> _pool = new List<AudioSource>();
        AudioSource _music, _voice, _follow;
        AudioLowPassFilter _followLp;
        AudioReverbFilter _followRv;
        Transform _followTarget;
        float _duckUntil;
        bool _spatializer;
        public bool Muted;
        /// <summary>Last clip names played, newest last. Read by tests and by the build log.</summary>
        public readonly List<string> History = new List<string>();

        public static AudioDirector Create(Transform stage)
        {
            var go = new GameObject("Audio");
            go.transform.SetParent(stage, false);
            var a = go.AddComponent<AudioDirector>();
            a.Build();
            return a;
        }

        void Build()
        {
            _spatializer = !string.IsNullOrEmpty(AudioSettings.GetSpatializerPluginName());
            foreach (var c in Resources.LoadAll<AudioClip>("Audio")) _clips[c.name] = c;

            _music = NewSource("Music");
            _music.loop = true; _music.volume = 0f;
            _music.minDistance = 0.35f; _music.maxDistance = 6f;
            _voice = NewSource("Voice");
            _voice.minDistance = 0.3f;

            _follow = NewSource("Follow");
            _followLp = _follow.gameObject.AddComponent<AudioLowPassFilter>();
            _followRv = _follow.gameObject.AddComponent<AudioReverbFilter>();
            _followLp.enabled = false; _followRv.enabled = false;
        }

        AudioSource NewSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.spatialize = _spatializer;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = 0.25f; s.maxDistance = 8f;
            s.dopplerLevel = 0f;
            return s;
        }

        public AudioClip Clip(string name) => name != null && _clips.TryGetValue(name, out var c) ? c : null;
        public bool Has(string name) => Clip(name) != null;

        /// <summary>One-shot at a world position.</summary>
        public void Play(string name, Vector3 worldPos, float volume = 1f, float pitch = 1f)
        {
            History.Add(name); if (History.Count > 64) History.RemoveAt(0);
            var clip = Clip(name);
            if (!clip || Muted) return;
            AudioSource s = null;
            foreach (var p in _pool) if (!p.isPlaying) { s = p; break; }
            if (!s) { s = NewSource("Sfx" + _pool.Count); _pool.Add(s); }
            s.transform.position = worldPos;
            s.pitch = pitch; s.volume = volume; s.clip = clip;
            s.Play();
        }

        /// <summary>The recorded line if it shipped, otherwise the wordless mood clip.</summary>
        AudioClip VoiceClip(Line line, out bool spoken)
        {
            var c = Clip(line.Spoken);
            spoken = c;
            return c ? c : Clip(line.Voice);
        }

        /// <summary>Length of the recorded line in seconds, 0 if only the wordless fallback exists.</summary>
        public float SpokenLength(Line line) { var c = VoiceClip(line, out bool spoken); return spoken ? c.length : 0f; }

        /// <summary>Speaks a line from the given position and ducks the music under it.</summary>
        public float Say(Line line, Vector3 worldPos)
        {
            var clip = VoiceClip(line, out bool spoken);
            History.Add(clip ? clip.name : ""); if (History.Count > 64) History.RemoveAt(0);
            if (!clip || Muted) return 0f;
            _voice.transform.position = worldPos;
            _voice.pitch = spoken ? 1f : Random.Range(0.97f, 1.04f);
            _voice.clip = clip; _voice.volume = spoken ? 1f : 0.9f;
            _voice.Play();
            _duckUntil = Time.time + clip.length + 0.35f;
            return clip.length;
        }

        public bool VoicePlaying => _voice && _voice.isPlaying;
        public bool MusicDucked => Time.time < _duckUntil;

        public void StartMusic(Vector3 worldPos)
        {
            var clip = Clip("music_loop");
            if (!clip) return;
            _music.transform.position = worldPos;
            if (_music.clip != clip) _music.clip = clip;
            if (!_music.isPlaying && !Muted) _music.Play();
        }

        public void MoveMusic(Vector3 worldPos) { _music.transform.position = worldPos; }

        /// <summary>
        /// A sound that travels with an object and sinks into the pit: as depth01 goes 0 → 1 the low-pass closes
        /// and the reverb opens, so the coin audibly falls away from the listener.
        /// </summary>
        public void PlayFollowing(string name, Transform target, float volume = 1f)
        {
            History.Add(name);
            var clip = Clip(name);
            _followTarget = target;
            if (!clip || Muted) return;
            _follow.clip = clip; _follow.volume = volume; _follow.pitch = 1f;
            _followLp.enabled = true; _followRv.enabled = true;
            SetFollowDepth(0f);
            _follow.Play();
        }

        public void SetFollowDepth(float depth01)
        {
            depth01 = Mathf.Clamp01(depth01);
            _followLp.cutoffFrequency = Mathf.Lerp(18000f, 2600f, depth01);
            _followRv.reverbPreset = AudioReverbPreset.User;
            _followRv.dryLevel = Mathf.Lerp(0f, -350f, depth01);
            _followRv.room = Mathf.Lerp(-2500f, -250f, depth01);
            _followRv.decayTime = Mathf.Lerp(0.6f, 2.4f, depth01);
            _followRv.reverbLevel = Mathf.Lerp(-1200f, 150f, depth01);
        }

        public void SetPaused(bool paused)
        {
            if (paused) { _music.Pause(); _voice.Pause(); _follow.Pause(); foreach (var p in _pool) p.Pause(); }
            else { _music.UnPause(); _voice.UnPause(); _follow.UnPause(); foreach (var p in _pool) p.UnPause(); }
        }

        void Update()
        {
            float target = Muted ? 0f : (MusicDucked ? DuckedVolume : MusicVolume);
            _music.volume = Mathf.MoveTowards(_music.volume, target, Time.deltaTime * 0.7f);
            if (_followTarget && _follow.isPlaying) _follow.transform.position = _followTarget.position;
        }
    }
}
