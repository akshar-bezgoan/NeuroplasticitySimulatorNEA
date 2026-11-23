namespace Neuroplasticity.Core;

public class Network
{
    public Layer[] Layers;   // [0]=input, [1..n-2]=hidden, [n-1]=output (softmax)
    public int Inputs, Classes;

    public Network(int inputs, int[] hidden, int classes, double range, Random rng)
    {
        Inputs = inputs; Classes = classes;
        var ls = new List<Layer> { new Layer(Enumerable.Range(0, inputs).Select(_ => (Neuron)new InputNeuron()).ToArray()) };
        int prev = inputs;
        foreach (int h in hidden)
        {
            int p = prev;
            ls.Add(new Layer(Enumerable.Range(0, h).Select(_ => (Neuron)new HiddenNeuron(p, range, rng)).ToArray()));
            prev = h;
        }
        int pp = prev;
        ls.Add(new Layer(Enumerable.Range(0, classes).Select(_ => (Neuron)new OutputNeuron(pp, range, rng)).ToArray(), true));
        Layers = ls.ToArray();
    }

    public double[] Forward(double[] x)
    {
        for (int i = 0; i < Inputs; i++) ((InputNeuron)Layers[0].Neurons[i]).Set(x[i]);
        double[] cur = x;
        for (int l = 1; l < Layers.Length; l++) cur = Layers[l].Forward(cur);
        return cur;
    }

    public int Predict(double[] x) => Act.ArgMax(Forward(x));

    public void Commit()
    {
        for (int l = 1; l < Layers.Length; l++) foreach (var n in Layers[l].Neurons) n.Commit();
    }

    public int AliveSynapses => Layers.Skip(1).Sum(l => l.Neurons.Sum(n => n.AliveCount));
    public int TotalSynapses => Layers.Skip(1).Sum(l => l.Neurons.Sum(n => n.W.Length));

    /// <summary>Grow or shrink the first hidden layer while running (network-size slider).</summary>
    public void ResizeFirstHidden(int n, double range, Random rng)
    {
        if (Layers.Length < 3 || n < 2 || n == Layers[1].Neurons.Length) return;
        var list = Layers[1].Neurons.ToList();
        int cur = list.Count;
        if (n > cur) for (int i = cur; i < n; i++) list.Add(new HiddenNeuron(Inputs, range, rng));
        else list.RemoveRange(n, cur - n);
        Layers[1].Neurons = list.ToArray();
        foreach (var m in Layers[2].Neurons) m.ResizeInputs(n, range, rng);
    }
}
