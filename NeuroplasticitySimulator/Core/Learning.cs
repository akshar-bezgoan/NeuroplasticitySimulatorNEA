namespace Neuroplasticity.Core;

public record DevelopmentStage(string Name, double WeightRange, int Pretrain, double LearningRate, double ForgettingRate);

public static class Stages
{
    // Values from spec section 2.4.1
    public static readonly DevelopmentStage[] All =
    {
        new("Infant", 0.50, 0,    0.050, 0.0005),
        new("Child",  0.40, 150,  0.050, 0.0010),
        new("Teen",   0.30, 600,  0.030, 0.0010),
        new("Adult",  0.25, 2000, 0.010, 0.0002),
    };
    public static DevelopmentStage ByName(string n) => All.First(s => s.Name == n);
}

/// <summary>
/// A learning rule inspects the last forward pass and sends weight-change requests to each neuron's
/// PendingDelta, multiplied by 'scale'. The Hybrid rule uses scale = alpha and 1 - alpha (spec 2.3.3).
/// </summary>
public interface IRule { void Propose(Network net, double[] x, int label, double lr, double scale); }

public class BackpropRule : IRule
{
    public double Lambda = 1e-4;   // L2 regularisation (spec 2.2.7)

    public void Propose(Network net, double[] x, int label, double lr, double scale)
    {
        var L = net.Layers; int last = L.Length - 1;
        for (int k = 0; k < L[last].Neurons.Length; k++)
            L[last].Neurons[k].Delta = L[last].Neurons[k].A - (k == label ? 1 : 0);   // softmax + cross-entropy
        for (int l = last - 1; l >= 1; l--)
            for (int i = 0; i < L[l].Neurons.Length; i++)
            {
                double s = 0;
                foreach (var m in L[l + 1].Neurons) if (i < m.W.Length && m.Alive[i]) s += m.W[i] * m.Delta;
                L[l].Neurons[i].Delta = s * L[l].Neurons[i].Slope();
            }
        for (int l = 1; l <= last; l++)
        {
            var prev = L[l - 1].Outputs();
            foreach (var n in L[l].Neurons)
            {
                for (int j = 0; j < n.W.Length; j++)
                    if (n.Alive[j]) n.Request(j, -scale * lr * (n.Delta * prev[j] + Lambda * n.W[j]));
                n.PendingBias += -scale * lr * n.Delta;
            }
        }
    }
}

/// <summary>Oja's rule in hidden layers; reward-gated, class-specific updates in the output layer (spec 2.3.2).</summary>
public class HebbianRule : IRule
{
    public void Propose(Network net, double[] x, int label, double lr, double scale)
    {
        var L = net.Layers; int last = L.Length - 1;
        for (int l = 1; l < last; l++)
        {
            var prev = L[l - 1].Outputs();
            foreach (var n in L[l].Neurons)
            {
                double y = n.A;
                for (int j = 0; j < n.W.Length; j++)
                    if (n.Alive[j]) n.Request(j, scale * lr * y * (prev[j] - y * n.W[j]));   // dw = eta*y*(x - y*w)
            }
        }
        var pOut = L[last - 1].Outputs();
        int pred = Act.ArgMax(L[last].Outputs());
        for (int k = 0; k < L[last].Neurons.Length; k++)
        {
            var n = L[last].Neurons[k];
            for (int j = 0; j < n.W.Length; j++)
            {
                if (!n.Alive[j]) continue;
                double d = 0;
                if (k == label) d = lr * (pOut[j] - n.W[j]);               // strengthen the correct pathway
                else if (k == pred) d = -0.5 * lr * pOut[j];               // weaken the wrong one
                n.Request(j, scale * d);
            }
        }
    }
}

public class HybridRule : IRule
{
    public double Alpha = 0.5;
    readonly BackpropRule bp = new(); readonly HebbianRule heb = new();
    public void Propose(Network net, double[] x, int label, double lr, double scale)
    {
        bp.Propose(net, x, label, lr, scale * Alpha);
        heb.Propose(net, x, label, lr, scale * (1 - Alpha));
    }
}

public static class Rules
{
    public static IRule Make(RuleKind k) => k switch { RuleKind.Backprop => new BackpropRule(), RuleKind.Hebbian => new HebbianRule(), _ => new HybridRule() };
}
