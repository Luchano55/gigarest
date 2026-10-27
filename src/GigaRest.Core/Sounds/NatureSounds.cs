namespace GigaRest.Core.Sounds;

/// <summary>Звуки природы для «Слушать».</summary>
public enum NatureSound { Rain, Sea, Fire, Wind }

/// <summary>
/// Звуки природы, которые программа считает сама (без записей и интернета): шум, фильтры и случайные «капли»,
/// «треск», «волны». Результат — кольцо в несколько десятков секунд, концы сведены плавно, поэтому по кругу
/// звучит без шва. 16 бит, стерео, 44,1 кГц.
/// </summary>
public static class NatureSounds
{
    public const int SampleRate = 44100;

    public static string Name(NatureSound s) => s switch
    {
        NatureSound.Rain => "Дождь",
        NatureSound.Sea => "Море",
        NatureSound.Fire => "Костёр",
        _ => "Ветер",
    };

    /// <summary>Кольцо звука: чередующиеся отсчёты левый/правый.</summary>
    public static short[] Generate(NatureSound sound, int seconds = 40, int seed = 7)
    {
        var fade = 3 * SampleRate;
        var n = seconds * SampleRate;
        var raw = new float[2][];
        for (var ch = 0; ch < 2; ch++)
        {
            var rnd = new Random(seed + ch * 101);
            raw[ch] = sound switch
            {
                NatureSound.Rain => Rain(n + fade, rnd),
                NatureSound.Sea => Sea(n + fade, rnd, ch),
                NatureSound.Fire => Fire(n + fade, rnd),
                _ => Wind(n + fade, rnd, ch),
            };
        }
        // Кольцо: хвост плавно накладывается на начало — по кругу без щелчка.
        // Громкость — по средней силе звука (все звуки примерно одинаково громкие), редкие пики — мягко скругляем.
        var outp = new short[n * 2];
        double sum = 0;
        for (var ch = 0; ch < 2; ch++)
            for (var i = 0; i < n; i++) sum += raw[ch][i] * raw[ch][i];
        var gain = (float)(0.13 / Math.Max(1e-6, Math.Sqrt(sum / (2.0 * n))));
        for (var ch = 0; ch < 2; ch++)
        {
            var x = raw[ch];
            for (var i = 0; i < n; i++)
            {
                var v = x[i];
                if (i < fade)
                {
                    var t = (float)i / fade;
                    v = x[i] * t + x[n + i] * (1 - t);
                }
                outp[i * 2 + ch] = (short)(MathF.Tanh(v * gain) * 0.95f * short.MaxValue);
            }
        }
        return outp;
    }

    /// <summary>WAV-файл (PCM, 16 бит, стерео) из отсчётов.</summary>
    public static byte[] ToWav(short[] samples)
    {
        var data = samples.Length * 2;
        using var ms = new MemoryStream(44 + data);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(SampleRate);
        w.Write(SampleRate * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data"u8); w.Write(data);
        foreach (var s in samples) w.Write(s);
        return ms.ToArray();
    }

    // ---------- сами звуки ----------

    // Дождь: ровный шелест (розовый шум без низа) и капли — короткие звонкие щелчки разной высоты.
    private static float[] Rain(int n, Random rnd)
    {
        var x = new float[n];
        var pink = new Pink();
        var hp = new Biquad(Biquad.HighPass(400, 0.7));
        for (var i = 0; i < n; i++) x[i] = hp.Next(pink.Next(rnd)) * 0.55f;
        var drops = n / SampleRate * 40;
        for (var d = 0; d < drops; d++)
        {
            var at = rnd.Next(n);
            var bp = new Biquad(Biquad.BandPass(1500 + rnd.NextDouble() * 4500, 6));
            var amp = (float)(0.15 + rnd.NextDouble() * 0.6);
            var len = SampleRate / 60;
            for (var i = 0; i < len && at + i < n; i++)
            {
                var env = MathF.Exp(-i / (len / 5f));
                x[at + i] += bp.Next((float)(rnd.NextDouble() * 2 - 1) * env) * amp * 3;
            }
        }
        return x;
    }

    // Море: низкий «бурый» шум, накатывающий волнами 7–11 с, на гребне — шипение пены.
    private static float[] Sea(int n, Random rnd, int ch)
    {
        var x = new float[n];
        var brown = 0f;
        var pink = new Pink();
        var foam = new Biquad(Biquad.BandPass(2500, 0.8));
        var phase = ch * 0.6;
        var period = 9.0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / SampleRate;
            phase += 1.0 / (period * SampleRate);
            if (phase >= 1) { phase -= 1; period = 7 + rnd.NextDouble() * 4; }
            var wave = Math.Pow((1 - Math.Cos(2 * Math.PI * phase)) / 2, 2);
            brown = (brown + 0.02f * (float)(rnd.NextDouble() * 2 - 1)) / 1.02f;
            var body = brown * 3.5f * (float)(0.25 + 0.75 * wave);
            var hiss = foam.Next(pink.Next(rnd)) * (float)(0.05 + 0.5 * Math.Pow(wave, 3));
            x[i] = body + hiss;
            _ = t;
        }
        return x;
    }

    // Костёр: низкое гудение пламени и потрескивание — редкие короткие щелчки и хлопки.
    private static float[] Fire(int n, Random rnd)
    {
        var x = new float[n];
        var brown = 0f;
        var lp = new Biquad(Biquad.LowPass(500, 0.7));
        for (var i = 0; i < n; i++)
        {
            brown = (brown + 0.02f * (float)(rnd.NextDouble() * 2 - 1)) / 1.02f;
            x[i] = lp.Next(brown) * 2.2f;
        }
        var cracks = n / SampleRate * 7;
        for (var c = 0; c < cracks; c++)
        {
            var at = rnd.Next(n);
            var burst = 1 + rnd.Next(4); // щелчок бывает «очередью»
            for (var b = 0; b < burst; b++)
            {
                var start = at + b * rnd.Next(SampleRate / 80, SampleRate / 25);
                var len = rnd.Next(SampleRate / 900, SampleRate / 150);
                var amp = (float)(0.3 + rnd.NextDouble() * 0.9);
                var hp = new Biquad(Biquad.HighPass(1200 + rnd.NextDouble() * 3000, 0.7));
                for (var i = 0; i < len && start + i < n; i++)
                    x[start + i] += hp.Next((float)(rnd.NextDouble() * 2 - 1)) * amp * MathF.Exp(-i / (len / 3f));
            }
        }
        return x;
    }

    // Ветер: шум через полосовой фильтр, частота и сила медленно «гуляют» порывами.
    private static float[] Wind(int n, Random rnd, int ch)
    {
        var x = new float[n];
        var pink = new Pink();
        var gust = 0.5;
        var target = 0.5;
        var bp = new Biquad(Biquad.BandPass(500, 1.2));
        for (var i = 0; i < n; i++)
        {
            if (i % (SampleRate / 2) == 0) target = 0.2 + rnd.NextDouble() * 0.8;
            gust += (target - gust) * 0.00004;
            if (i % 64 == 0) bp.Set(Biquad.BandPass(250 + gust * 700 + ch * 40, 1.0 + gust));
            x[i] = bp.Next(pink.Next(rnd)) * (float)(0.2 + gust * 1.2);
        }
        return x;
    }

    /// <summary>Розовый шум (фильтр Пола Келлета) — мягче белого.</summary>
    private sealed class Pink
    {
        private float _b0, _b1, _b2, _b3, _b4, _b5, _b6;

        public float Next(Random rnd)
        {
            var w = (float)(rnd.NextDouble() * 2 - 1);
            _b0 = 0.99886f * _b0 + w * 0.0555179f;
            _b1 = 0.99332f * _b1 + w * 0.0750759f;
            _b2 = 0.96900f * _b2 + w * 0.1538520f;
            _b3 = 0.86650f * _b3 + w * 0.3104856f;
            _b4 = 0.55000f * _b4 + w * 0.5329522f;
            _b5 = -0.7616f * _b5 - w * 0.0168980f;
            var p = _b0 + _b1 + _b2 + _b3 + _b4 + _b5 + _b6 + w * 0.5362f;
            _b6 = w * 0.115926f;
            return p * 0.11f;
        }
    }

    /// <summary>Биквадратный фильтр (формулы RBJ).</summary>
    private sealed class Biquad
    {
        private double _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

        public Biquad((double, double, double, double, double, double) c) => Set(c);

        public void Set((double b0, double b1, double b2, double a0, double a1, double a2) c)
        {
            _b0 = c.b0 / c.a0; _b1 = c.b1 / c.a0; _b2 = c.b2 / c.a0; _a1 = c.a1 / c.a0; _a2 = c.a2 / c.a0;
        }

        public float Next(float x)
        {
            var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
            return (float)y;
        }

        private static (double w, double alpha) W(double f, double q)
        {
            var w = 2 * Math.PI * f / SampleRate;
            return (w, Math.Sin(w) / (2 * q));
        }

        public static (double, double, double, double, double, double) LowPass(double f, double q)
        {
            var (w, a) = W(f, q); var c = Math.Cos(w);
            return ((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + a, -2 * c, 1 - a);
        }

        public static (double, double, double, double, double, double) HighPass(double f, double q)
        {
            var (w, a) = W(f, q); var c = Math.Cos(w);
            return ((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + a, -2 * c, 1 - a);
        }

        public static (double, double, double, double, double, double) BandPass(double f, double q)
        {
            var (w, a) = W(f, q); var c = Math.Cos(w);
            return (a, 0, -a, 1 + a, -2 * c, 1 - a);
        }
    }
}
