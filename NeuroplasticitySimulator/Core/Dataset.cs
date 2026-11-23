using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Neuroplasticity.Core;

public class Sample { public double[] X; public int Y; }

public class Dataset
{
    public string Name;
    public int Side;                 // images are Side x Side, greyscale
    public int Inputs => Side * Side;
    public int Classes;
    public string[] ClassNames;
    public List<Sample> Train = new(), Test = new();

    // ---- built-in: noisy, shifted 8x8 digits (stand-in for the prototype's scikit-learn digits) ----
    static readonly string[][] Glyphs = new[]
    {
        "01110 10001 10011 10101 11001 10001 01110", "00100 01100 00100 00100 00100 00100 01110",
        "01110 10001 00001 00010 00100 01000 11111", "11110 00001 00001 01110 00001 00001 11110",
        "00010 00110 01010 10010 11111 00010 00010", "11111 10000 11110 00001 00001 10001 01110",
        "00110 01000 10000 11110 10001 10001 01110", "11111 00001 00010 00100 01000 01000 01000",
        "01110 10001 10001 01110 10001 10001 01110", "01110 10001 10001 01111 00001 00010 01100",
    }.Select(s => s.Split(' ')).ToArray();

    public static Dataset Load(string kind, string path) => kind switch
    {
        "cifar" => Cifar(path),
        "folder" => FromFolder(path),
        _ => Digits()
    };

    public static Dataset Digits()
    {
        var rng = new Random(42);
        var d = new Dataset { Name = "Digits (built-in)", Side = 8, Classes = 10, ClassNames = Enumerable.Range(0, 10).Select(i => i.ToString()).ToArray() };
        for (int i = 0; i < 2000; i++) (i < 1700 ? d.Train : d.Test).Add(MakeDigit(i % 10, rng));
        return d;
    }

    static Sample MakeDigit(int digit, Random rng)
    {
        var x = new double[64];
        int ox = rng.Next(4), oy = rng.Next(2);
        for (int r = 0; r < 7; r++) for (int c = 0; c < 5; c++)
            if (Glyphs[digit][r][c] == '1') x[(r + oy) * 8 + c + ox] = 0.7 + rng.NextDouble() * 0.3;
        for (int i = 0; i < 64; i++) if (rng.NextDouble() < 0.08) x[i] = x[i] > 0 ? 0 : rng.NextDouble() * 0.8;
        return new Sample { X = x, Y = digit };
    }

    // ---- CIFAR-10 binary batch (data_batch_1.bin): greyscale 32x32 subset ----
    public static Dataset Cifar(string file, int max = 2500)
    {
        var b = File.ReadAllBytes(file);
        const int rec = 3073;
        int n = Math.Min(max, b.Length / rec);
        if (n < 100) throw new InvalidDataException("Not a CIFAR-10 binary batch file.");
        var d = new Dataset { Name = "CIFAR-10 (grey)", Side = 32, Classes = 10,
            ClassNames = new[] { "airplane", "automobile", "bird", "cat", "deer", "dog", "frog", "horse", "ship", "truck" } };
        for (int i = 0; i < n; i++)
        {
            int o = i * rec; var x = new double[1024];
            for (int p = 0; p < 1024; p++)
                x[p] = (0.299 * b[o + 1 + p] + 0.587 * b[o + 1025 + p] + 0.114 * b[o + 2049 + p]) / 255.0;
            (i < n * 0.8 ? d.Train : d.Test).Add(new Sample { X = x, Y = b[o] });
        }
        return d;
    }

    // ---- user images: one sub-folder per class, resized to 16x16 greyscale ----
    public static Dataset FromFolder(string path, int side = 16)
    {
        var dirs = Directory.GetDirectories(path).OrderBy(p => p).ToArray();
        if (dirs.Length < 2) throw new InvalidDataException("Choose a folder containing at least two sub-folders (one per class).");
        var d = new Dataset { Name = "My images", Side = side, Classes = dirs.Length, ClassNames = dirs.Select(Path.GetFileName).ToArray() };
        var all = new List<Sample>();
        var exts = new HashSet<string> { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
        for (int c = 0; c < dirs.Length; c++)
            foreach (var f in Directory.GetFiles(dirs[c]).Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())))
            {
                try
                {
                    using var img = Image.Load<L8>(f);
                    img.Mutate(m => m.Resize(side, side));
                    var x = new double[side * side];
                    for (int yy = 0; yy < side; yy++) for (int xx = 0; xx < side; xx++) x[yy * side + xx] = img[xx, yy].PackedValue / 255.0;
                    all.Add(new Sample { X = x, Y = c });
                }
                catch { /* skip unreadable files */ }
            }
        if (all.Count < 20) throw new InvalidDataException("Need at least 20 readable images in total.");
        var rng = new Random(1);
        all = all.OrderBy(_ => rng.Next()).ToList();
        int cut = (int)(all.Count * 0.8);
        d.Train.AddRange(all.Take(cut)); d.Test.AddRange(all.Skip(cut));
        return d;
    }
}
