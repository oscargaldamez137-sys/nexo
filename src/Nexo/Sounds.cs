using System;
using System.Collections.Generic;
using System.IO;
using System.Media;

namespace Nexo
{
    /// <summary>Efectos cortos sintetizados en memoria (sin archivos de audio).</summary>
    internal sealed class Sounds
    {
        private const int Rate = 44100;
        private readonly Dictionary<string, byte[]> _cache = new Dictionary<string, byte[]>();
        private SoundPlayer _player;

        public bool Muted { get; set; }

        public void Move(int index) => Play("move" + (index % 8), () => Tone(520 + (index % 8) * 40, 0.07, Wave.Triangle, 0.22));
        public void Enter() => Play("enter", () => Concat(Tone(660, 0.06, Wave.Sine, 0.25), Tone(990, 0.08, Wave.Sine, 0.22)));
        public void Back() => Play("back", () => Concat(Tone(700, 0.05, Wave.Sine, 0.22), Tone(470, 0.08, Wave.Sine, 0.2)));
        public void Confirm() => Play("confirm", () => Concat(Slide(440, 880, 0.12, 0.28), Slide(880, 1320, 0.16, 0.22)));
        public void Error() => Play("error", () => Slide(220, 150, 0.22, 0.18, Wave.Square));

        private void Play(string key, Func<short[]> make)
        {
            if (Muted) return;
            try
            {
                if (!_cache.TryGetValue(key, out var wav))
                {
                    wav = ToWav(make());
                    _cache[key] = wav;
                }
                // Se libera el reproductor anterior y su flujo antes de sustituirlo.
                if (_player != null)
                {
                    _player.Stop();
                    _player.Stream?.Dispose();
                    _player.Dispose();
                }
                _player = new SoundPlayer(new MemoryStream(wav));
                _player.Play();
            }
            catch
            {
                // Sin dispositivo de audio: Nexo sigue funcionando en silencio.
            }
        }

        private enum Wave { Sine, Triangle, Square }

        private static short[] Tone(double freq, double seconds, Wave wave, double gain) => Slide(freq, freq, seconds, gain, wave);

        private static short[] Slide(double from, double to, double seconds, double gain, Wave wave = Wave.Sine)
        {
            int n = (int)(Rate * seconds);
            var samples = new short[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / n;
                double freq = from * Math.Pow(to / from, t);
                phase += 2 * Math.PI * freq / Rate;
                double s;
                switch (wave)
                {
                    case Wave.Triangle: s = 2 / Math.PI * Math.Asin(Math.Sin(phase)); break;
                    case Wave.Square: s = Math.Sign(Math.Sin(phase)) * 0.6; break;
                    default: s = Math.Sin(phase); break;
                }
                // Ataque rápido y caída exponencial: suena a "clic" suave, no a pitido.
                double envelope = Math.Min(1, i / (Rate * 0.006)) * Math.Exp(-4.5 * t);
                samples[i] = (short)(s * envelope * gain * short.MaxValue);
            }
            return samples;
        }

        private static short[] Concat(short[] a, short[] b)
        {
            var result = new short[a.Length + b.Length];
            a.CopyTo(result, 0);
            b.CopyTo(result, a.Length);
            return result;
        }

        private static byte[] ToWav(short[] samples)
        {
            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory))
            {
                int bytes = samples.Length * 2;
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + bytes);
                writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);      // PCM
                writer.Write((short)1);      // mono
                writer.Write(Rate);
                writer.Write(Rate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(bytes);
                foreach (var s in samples) writer.Write(s);
                writer.Flush();
                return memory.ToArray();
            }
        }
    }
}
