using System;
using UnityEngine;

namespace FlightRecord.Experiments
{
    /// <summary>
    /// Experiment 06's Unity-native capture test: reads a microphone with Unity's own
    /// <see cref="Microphone"/> API and reports the frame on which a sharp transient (a clap)
    /// first becomes visible. Comparing that moment with when the same clap arrives over the
    /// bridge (both on the game's clock) shows whether native capture is fast enough to
    /// replace the Python sidecar (D-101). A peak detector, not the feature pipeline: this
    /// measures how soon the samples are available, which bounds any analysis done on them.
    /// </summary>
    public sealed class MicrophoneProbe : IDisposable
    {
        const float MinPeak = 0.1f;      // ignore anything quieter than this (full scale = 1)
        const float NoiseRatio = 8f;     // and anything less than this many times the noise floor
        const double Refractory = 0.15;  // seconds; one onset per clap

        readonly AudioClip _clip;
        readonly int _channels;
        int _last;
        float _noise = 0.01f;
        double _lastOnset = double.NegativeInfinity;

        public string Device { get; }
        public int SampleRate { get; }
        public int Onsets { get; private set; }

        /// <param name="device">A name from <see cref="Microphone.devices"/>; null or empty for the default.</param>
        public MicrophoneProbe(string device, int sampleRate = 48000)
        {
            Device = string.IsNullOrEmpty(device) ? null : device;
            Microphone.GetDeviceCaps(Device, out int min, out int max);
            SampleRate = min == 0 && max == 0 ? sampleRate : Mathf.Clamp(sampleRate, min, max);
            _clip = Microphone.Start(Device, true, 2, SampleRate);
            if (_clip == null) throw new InvalidOperationException("Microphone.Start failed");
            _channels = Math.Max(1, _clip.channels);
        }

        /// <summary>Read what arrived since the last call; true, with the time, if a clap starts in it.</summary>
        public bool Poll(double now, out double seen)
        {
            seen = 0;
            int pos = Microphone.GetPosition(Device);
            if (pos < 0) return false;
            int total = _clip.samples;
            int n = (pos - _last + total) % total;
            if (n == 0) return false;
            var samples = new float[n * _channels];
            _clip.GetData(samples, _last); // wraps around the looping clip
            _last = pos;

            float peak = 0;
            foreach (float s in samples) peak = Math.Max(peak, Math.Abs(s));
            if (peak > Math.Max(MinPeak, NoiseRatio * _noise) && now - _lastOnset > Refractory)
            {
                _lastOnset = seen = now;
                Onsets++;
                return true;
            }
            _noise = 0.9f * _noise + 0.1f * peak;
            return false;
        }

        public void Dispose() => Microphone.End(Device);
    }
}
