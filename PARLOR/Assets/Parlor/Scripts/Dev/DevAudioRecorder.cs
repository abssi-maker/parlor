using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Parlor
{
    /// <summary>
    /// Development only: records the final audio mix heard by the AudioListener to a 16-bit WAV, for the demo video
    /// (the Mac has no loopback device, so a screen recording would be silent). Writes "<path>.start" with the UTC
    /// epoch seconds of the first recorded sample so the WAV can be aligned with a wall-clock screen recording.
    /// Usage from the Editor: DevAudioRecorder.Begin("/tmp/show.wav"); ... DevAudioRecorder.End();
    /// </summary>
    public sealed class DevAudioRecorder : MonoBehaviour
    {
        static DevAudioRecorder _active;
        readonly List<float> _samples = new List<float>(48000 * 2 * 300);
        readonly object _lock = new object();
        string _path;
        int _channels, _rate;
        double _startEpoch = -1;
        bool _recording;

        public static string Begin(string path)
        {
            var listener = FindFirstObjectByType<AudioListener>();
            if (!listener) return "no AudioListener";
            if (_active) Destroy(_active);
            _active = listener.gameObject.AddComponent<DevAudioRecorder>();
            _active._path = path;
            _active._rate = AudioSettings.outputSampleRate;
            _active._recording = true;
            return "recording " + _active._rate + " Hz to " + path;
        }

        /// <summary>While recording, appends "epoch-seconds message" to "&lt;path&gt;.events" (beats and gates, for cutting the video).</summary>
        public static void Event(string msg)
        {
            if (!_active || string.IsNullOrEmpty(_active._path)) return;
            double now = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            try { File.AppendAllText(_active._path + ".events", now.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " " + msg + "\n"); }
            catch (IOException) { }
        }

        public static string End()
        {
            if (!_active) return "not recording";
            var r = _active; _active = null;
            r._recording = false;
            float[] data;
            lock (r._lock) data = r._samples.ToArray();
            int ch = Mathf.Max(1, r._channels);
            using (var w = new BinaryWriter(File.Create(r._path)))
            {
                int bytes = data.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes); w.Write(new[] { 'W', 'A', 'V', 'E' });
                w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)ch);
                w.Write(r._rate); w.Write(r._rate * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
                foreach (float s in data) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
            }
            File.WriteAllText(r._path + ".start", r._startEpoch.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            Destroy(r);
            return "wrote " + (data.Length / ch / (float)r._rate).ToString("F1") + " s, " + ch + " ch";
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_recording) return;
            lock (_lock)
            {
                if (_startEpoch < 0)
                {
                    // the buffer being filled now is heard about one DSP buffer later; close enough for lip-sync at 30 fps
                    _startEpoch = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                    _channels = channels;
                }
                _samples.AddRange(data);
            }
        }
    }
}
