namespace Neuroplasticity.Core;

/// <summary>Abstract neuron (spec 2.2.3): dot product + bias, then activation. Subclasses differ in activation.</summary>
public abstract class Neuron
{
    public double[] W;          // synaptic weights
    public bool[] Alive;        // false = pruned or lesioned (no signal)
    public double Bias;
    public double Z, A, Delta;  // pre-activation, activation, backprop error term
    public double[] Pending;    // PendingDelta: weight-change requests from learning rules (spec 2.3.3)
    public double PendingBias;
    public bool Lesioned;

    protected Neuron(int nIn, double range, Random rng)
    {
        W = new double[nIn]; Alive = new bool[nIn]; Pending = new double[nIn];
        for (int i = 0; i < nIn; i++) { W[i] = (rng.NextDouble() * 2 - 1) * range; Alive[i] = true; }
    }

    public double Forward(double[] x)
    {
        double z = Bias;
        for (int j = 0; j < W.Length; j++) if (Alive[j]) z += W[j] * x[j];
        Z = z; A = Activate(z);
        return A;
    }

    protected abstract double Activate(double z);
    public abstract double Slope();

    public void Request(int j, double d) => Pending[j] += d;

    public void Commit()
    {
        for (int j = 0; j < W.Length; j++) { if (Alive[j]) W[j] += Pending[j]; Pending[j] = 0; }
        Bias += PendingBias; PendingBias = 0;
    }

    public int AliveCount => Alive.Count(a => a);
    public void Kill(int j) { Alive[j] = false; W[j] = 0; Pending[j] = 0; }
    public void Lesion() { for (int j = 0; j < W.Length; j++) Kill(j); Bias = 0; Lesioned = true; }

    public double MeanAbsWeight()
    {
        double s = 0; int n = 0;
        for (int j = 0; j < W.Length; j++) if (Alive[j]) { s += Math.Abs(W[j]); n++; }
        return n == 0 ? 0 : s / n;
    }

    public void ResizeInputs(int n, double range, Random rng)
    {
        int old = W.Length;
        Array.Resize(ref W, n); Array.Resize(ref Alive, n); Array.Resize(ref Pending, n);
        for (int i = old; i < n; i++) { W[i] = (rng.NextDouble() * 2 - 1) * range; Alive[i] = !Lesioned; }
    }
}

public class InputNeuron : Neuron
{
    public InputNeuron() : base(0, 0, null) { }
    public void Set(double v) { A = v; Z = v; }
    protected override double Activate(double z) => z;
    public override double Slope() => 1;
}

/// <summary>Leaky ReLU so injured networks can still recover (no dead neurons).</summary>
public class HiddenNeuron : Neuron
{
    public HiddenNeuron(int nIn, double range, Random rng) : base(nIn, range, rng) { }
    protected override double Activate(double z) => Act.Leaky(z);
    public override double Slope() => Act.LeakyDeriv(Z);
}

/// <summary>Output neuron: raw score; softmax is applied across the whole layer.</summary>
public class OutputNeuron : Neuron
{
    public OutputNeuron(int nIn, double range, Random rng) : base(nIn, range, rng) { }
    protected override double Activate(double z) => z;
    public override double Slope() => 1;
}

/// <summary>A layer holds one type of neuron and organises their connections (spec 2.2.5).</summary>
public class Layer
{
    public Neuron[] Neurons;
    public bool IsSoftmax;
    public Layer(Neuron[] n, bool softmax = false) { Neurons = n; IsSoftmax = softmax; }

    public double[] Forward(double[] x)
    {
        var o = new double[Neurons.Length];
        for (int i = 0; i < o.Length; i++) o[i] = Neurons[i].Forward(x);
        if (IsSoftmax)
        {
            o = Act.Softmax(o);
            for (int i = 0; i < o.Length; i++) Neurons[i].A = o[i];
        }
        return o;
    }

    public double[] Outputs() { var o = new double[Neurons.Length]; for (int i = 0; i < o.Length; i++) o[i] = Neurons[i].A; return o; }
}
