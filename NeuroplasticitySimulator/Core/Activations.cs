namespace Neuroplasticity.Core;

/// <summary>Activation functions (spec 2.2.4).</summary>
public static class Act
{
    public const double LeakAlpha = 0.01;
    public static double Leaky(double x) => x >= 0 ? x : LeakAlpha * x;
    public static double LeakyDeriv(double z) => z >= 0 ? 1.0 : LeakAlpha;

    public static double[] Softmax(double[] z)
    {
        double m = z.Max();
        var e = z.Select(v => Math.Exp(v - m)).ToArray();
        double s = e.Sum();
        for (int i = 0; i < e.Length; i++) e[i] /= s;
        return e;
    }

    public static int ArgMax(double[] a)
    {
        int b = 0;
        for (int i = 1; i < a.Length; i++) if (a[i] > a[b]) b = i;
        return b;
    }
}
