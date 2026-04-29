using UnityEngine;

namespace AGVRSystem.Audio
{
    /// <summary>
    /// Generates procedural audio tones at runtime (beeps, dings, buzzes) without requiring
    /// pre-recorded AudioClips. Creates AudioClips from sine/square waves on demand and caches them.
    /// </summary>
    public static class ProceduralToneGenerator
    {
        private const int SampleRate = 44100;

        /// <summary>Tone waveform shape.</summary>
        public enum WaveShape
        {
            Sine,
            Square,
            Triangle,
            SoftSine // Sine with smooth attack/release envelope
        }

        /// <summary>
        /// Creates a procedural AudioClip with the given parameters.
        /// </summary>
        /// <param name="name">Clip name for identification.</param>
        /// <param name="frequency">Tone frequency in Hz.</param>
        /// <param name="duration">Duration in seconds.</param>
        /// <param name="volume">Volume 0-1.</param>
        /// <param name="shape">Waveform shape.</param>
        public static AudioClip CreateTone(string name, float frequency, float duration,
            float volume = 0.5f, WaveShape shape = WaveShape.SoftSine)
        {
            int sampleCount = Mathf.CeilToInt(SampleRate * duration);
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / SampleRate;
                float normalizedT = (float)i / sampleCount;

                // Generate waveform
                float sample = GenerateSample(t, frequency, shape);

                // Apply envelope (smooth attack/release to avoid clicks)
                float envelope = CalculateEnvelope(normalizedT, duration);

                samples[i] = sample * volume * envelope;
            }

            AudioClip clip = AudioClip.Create(name, sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// Creates a two-tone "ding" sound (frequency sweep up).
        /// </summary>
        public static AudioClip CreateDing(string name, float baseFreq = 880f,
            float duration = 0.15f, float volume = 0.4f)
        {
            int sampleCount = Mathf.CeilToInt(SampleRate * duration);
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / SampleRate;
                float normalizedT = (float)i / sampleCount;

                // Frequency rises slightly for a "ding" feel
                float freq = baseFreq + (baseFreq * 0.5f * normalizedT);
                float sample = Mathf.Sin(2f * Mathf.PI * freq * t);

                // Quick attack, longer release
                float envelope = Mathf.Exp(-normalizedT * 6f);

                samples[i] = sample * volume * envelope;
            }

            AudioClip clip = AudioClip.Create(name, sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// Creates a success "fanfare" sound (ascending three-note chord).
        /// </summary>
        public static AudioClip CreateSuccessChime(string name, float duration = 0.6f,
            float volume = 0.35f)
        {
            int sampleCount = Mathf.CeilToInt(SampleRate * duration);
            float[] samples = new float[sampleCount];

            float[] frequencies = { 523.25f, 659.25f, 783.99f }; // C5, E5, G5
            float noteLength = duration / frequencies.Length;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / SampleRate;
                float normalizedT = (float)i / sampleCount;

                int noteIndex = Mathf.Min((int)(normalizedT * frequencies.Length), frequencies.Length - 1);
                float noteT = (t - noteIndex * noteLength) / noteLength;

                float sample = Mathf.Sin(2f * Mathf.PI * frequencies[noteIndex] * t);

                // Each note has its own envelope
                float noteEnvelope = Mathf.Exp(-noteT * 4f);
                float globalEnvelope = CalculateEnvelope(normalizedT, duration);

                samples[i] = sample * volume * noteEnvelope * globalEnvelope;
            }

            AudioClip clip = AudioClip.Create(name, sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// Creates an error/buzz sound (low frequency with harmonics).
        /// </summary>
        public static AudioClip CreateErrorBuzz(string name, float duration = 0.3f,
            float volume = 0.3f)
        {
            int sampleCount = Mathf.CeilToInt(SampleRate * duration);
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / SampleRate;
                float normalizedT = (float)i / sampleCount;

                // Low frequency buzz with harmonics
                float sample = Mathf.Sin(2f * Mathf.PI * 150f * t) * 0.6f
                             + Mathf.Sin(2f * Mathf.PI * 200f * t) * 0.3f
                             + Mathf.Sin(2f * Mathf.PI * 100f * t) * 0.1f;

                float envelope = CalculateEnvelope(normalizedT, duration);

                samples[i] = sample * volume * envelope;
            }

            AudioClip clip = AudioClip.Create(name, sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// Creates a countdown beep (short, clean tone).
        /// </summary>
        public static AudioClip CreateCountdownBeep(string name, float frequency = 1000f,
            float duration = 0.08f, float volume = 0.35f)
        {
            return CreateTone(name, frequency, duration, volume, WaveShape.SoftSine);
        }

        /// <summary>
        /// Creates a gentle ambient pad tone for background atmosphere.
        /// </summary>
        public static AudioClip CreateAmbientPad(string name, float duration = 10f,
            float volume = 0.08f)
        {
            int sampleCount = Mathf.CeilToInt(SampleRate * duration);
            float[] samples = new float[sampleCount];

            // Layered sine waves at harmonious intervals (C3, E3, G3, C4)
            float[] freqs = { 130.81f, 164.81f, 196.0f, 261.63f };
            float[] amps = { 0.4f, 0.25f, 0.2f, 0.15f };

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / SampleRate;
                float normalizedT = (float)i / sampleCount;

                float sample = 0f;
                for (int f = 0; f < freqs.Length; f++)
                {
                    // Subtle frequency modulation for organic feel
                    float modFreq = freqs[f] + Mathf.Sin(t * 0.3f * (f + 1)) * 0.5f;
                    sample += Mathf.Sin(2f * Mathf.PI * modFreq * t) * amps[f];
                }

                // Long fade in/out
                float fadeIn = Mathf.Clamp01(normalizedT * 5f);
                float fadeOut = Mathf.Clamp01((1f - normalizedT) * 5f);

                samples[i] = sample * volume * fadeIn * fadeOut;
            }

            AudioClip clip = AudioClip.Create(name, sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static float GenerateSample(float t, float frequency, WaveShape shape)
        {
            float phase = 2f * Mathf.PI * frequency * t;

            switch (shape)
            {
                case WaveShape.Sine:
                    return Mathf.Sin(phase);

                case WaveShape.Square:
                    return Mathf.Sin(phase) >= 0f ? 1f : -1f;

                case WaveShape.Triangle:
                    return 2f * Mathf.Abs(2f * ((frequency * t) - Mathf.Floor((frequency * t) + 0.5f))) - 1f;

                case WaveShape.SoftSine:
                    return Mathf.Sin(phase);

                default:
                    return Mathf.Sin(phase);
            }
        }

        private static float CalculateEnvelope(float normalizedT, float duration)
        {
            const float AttackTime = 0.02f;
            const float ReleaseTime = 0.05f;

            float attackNorm = AttackTime / duration;
            float releaseNorm = 1f - (ReleaseTime / duration);

            if (normalizedT < attackNorm)
            {
                return normalizedT / attackNorm;
            }
            else if (normalizedT > releaseNorm)
            {
                return (1f - normalizedT) / (1f - releaseNorm);
            }

            return 1f;
        }
    }
}
