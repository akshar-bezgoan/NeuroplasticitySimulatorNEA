namespace Neuroplasticity.Core;

public enum RuleKind { Backprop, Hebbian, Hybrid }

public class MetricPoint
{
    public int Cycle { get; set; }
    public double Accuracy { get; set; }
    public double Loss { get; set; }
    public int Alive { get; set; }
}

public class SimEvent
{
    public int Cycle { get; set; }
    public string Label { get; set; } = "";
}

/// <summary>One simulated brain: network + plasticity engine + development stage (spec 2.4).</summary>
public class Agent
{
    public const double PruneThreshold = 0.004;
    public string Id;
    public Dataset Data;
    public DevelopmentStage Stage;
    public RuleKind Kind;
    public IRule Rule;
    public Network Net;
    public double LearningRate, ForgettingRate;
    public int Cycle;
    public bool PlasticityEnabled = true;   // 'Enable Plasticity' switch: off = weights frozen
    public Sample LastSample;
    public int LastPred;
    public List<MetricPoint> History = new();
    public List<SimEvent> Events = new();
    readonly Random rng = new();
    readonly Queue<double> recent = new();

    public Agent(string id, Dataset data, DevelopmentStage stage, RuleKind kind, int hidden, bool pretrain = true)
    {
        Id = id; Data = data; Stage = stage; Kind = kind; Rule = Rules.Make(kind);
        LearningRate = stage.LearningRate; ForgettingRate = stage.ForgettingRate;
        Net = new Network(data.Inputs, new[] { hidden }, data.Classes, stage.WeightRange, rng);
        if (pretrain)
        {
            var bp = new BackpropRule();   // "prior experience" before the simulation starts
            for (int i = 0; i < stage.Pretrain; i++)
            {
                var s = data.Train[rng.Next(data.Train.Count)];
                Net.Forward(s.X); bp.Propose(Net, s.X, s.Y, 0.05, 1.0); Net.Commit();
            }
        }
        Record();
    }

    /// <summary>One pass of the simulation cycle (spec 2.4.2).</summary>
    public void Step()
    {
        var s = Data.Train[rng.Next(Data.Train.Count)];
        LastSample = s;
        var p = Net.Forward(s.X);
        LastPred = Act.ArgMax(p);
        recent.Enqueue(-Math.Log(Math.Max(p[s.Y], 1e-9)));
        if (recent.Count > 50) recent.Dequeue();
        if (PlasticityEnabled)
        {
            Rule.Propose(Net, s.X, s.Y, LearningRate, 1.0);
            Net.Commit();
            Forget(s.Y);
        }
        Cycle++;
        if (Cycle % 10 == 0) Record();
    }

    /// <summary>Unreinforced synapses decay; strong ones decay more slowly; near-zero ones are pruned (spec 2.3.4).</summary>
    void Forget(int label)
    {
        if (ForgettingRate <= 0) return;
        int last = Net.Layers.Length - 1;
        for (int l = 1; l <= last; l++)
        {
            var prev = Net.Layers[l - 1].Outputs();
            var neurons = Net.Layers[l].Neurons;
            for (int k = 0; k < neurons.Length; k++)
            {
                var n = neurons[k];
                for (int j = 0; j < n.W.Length; j++)
                {
                    if (!n.Alive[j]) continue;
                    bool used = Math.Abs(prev[j]) > 0.3 && (l == last ? k == label : n.A > 0.1);
                    if (used) continue;
                    n.W[j] *= 1 - ForgettingRate / (1 + 4 * Math.Abs(n.W[j]));
                    if (Math.Abs(n.W[j]) < PruneThreshold) n.Kill(j);
                }
            }
        }
    }

    public double Evaluate()
    {
        int n = Math.Min(200, Data.Test.Count), c = 0;
        for (int i = 0; i < n; i++) if (Net.Predict(Data.Test[i].X) == Data.Test[i].Y) c++;
        return 100.0 * c / Math.Max(1, n);
    }

    public void Record()
    {
        double acc = Evaluate();
        if (LastSample != null) Net.Forward(LastSample.X);   // restore the current task's activations for the visualisation
        History.Add(new MetricPoint
        {
            Cycle = Cycle, Accuracy = acc,
            Loss = recent.Count > 0 ? recent.Average() : Math.Log(Data.Classes), Alive = Net.AliveSynapses
        });
    }

    void Log(string label) { Events.Add(new SimEvent { Cycle = Cycle, Label = label }); Record(); }

    /// <summary>Simulated brain injury: remove a random fraction of surviving synapses (spec 2.3.5).</summary>
    public void Injure(double fraction = 0.4)
    {
        var list = new List<(Neuron n, int j)>();
        for (int l = 1; l < Net.Layers.Length; l++)
            foreach (var n in Net.Layers[l].Neurons)
                for (int j = 0; j < n.W.Length; j++) if (n.Alive[j]) list.Add((n, j));
        int k = (int)(list.Count * fraction);
        for (int i = 0; i < k; i++)
        {
            int r = rng.Next(i, list.Count);
            (list[i], list[r]) = (list[r], list[i]);
            list[i].n.Kill(list[i].j);
        }
        Log($"Injury: {k} synapses removed");
    }

    /// <summary>Damage one neuron: fraction 1.0 disables it, less removes that share of its remaining incoming synapses.</summary>
    public void InjureNeuron(int layer, int index, double fraction = 1.0)
    {
        if (layer < 1 || layer >= Net.Layers.Length || index >= Net.Layers[layer].Neurons.Length) return;
        var n = Net.Layers[layer].Neurons[index];
        if (fraction >= 1.0) { n.Lesion(); Log($"Injury: neuron {index} (layer {layer}) disabled"); return; }
        var alive = Enumerable.Range(0, n.W.Length).Where(j => n.Alive[j]).OrderBy(_ => rng.Next()).ToList();
        foreach (int j in alive.Take((int)(alive.Count * fraction))) n.Kill(j);
        Log($"Injury: {fraction:P0} of neuron {index} damaged");
    }

    public void ApplyStage(DevelopmentStage st) { Stage = st; LearningRate = st.LearningRate; ForgettingRate = st.ForgettingRate; Log($"Stage changed to {st.Name}"); }
    public void SetRule(RuleKind k) { Kind = k; Rule = Rules.Make(k); Log($"Rule changed to {k}"); }
    public void Resize(int hidden) { Net.ResizeFirstHidden(hidden, Stage.WeightRange, rng); Log($"Hidden layer resized to {hidden}"); }

    // ---- persistence helpers (spec 2.6.3.1) ----
    public SimulationSnapshot ToSnapshot() => new()
    {
        AgentId = Id, StageName = Stage.Name, RuleName = Kind.ToString(), DatasetName = Data.Name,
        Cycle = Cycle, LearningRate = LearningRate, ForgettingRate = ForgettingRate,
        LayerSizes = Net.Layers.Select(l => l.Neurons.Length).ToArray(),
        Layers = Net.Layers.Skip(1).Select(l => new LayerState
        {
            W = l.Neurons.Select(n => n.W.ToArray()).ToArray(),
            Alive = l.Neurons.Select(n => n.Alive.ToArray()).ToArray(),
            Bias = l.Neurons.Select(n => n.Bias).ToArray(),
            Lesioned = l.Neurons.Select(n => n.Lesioned).ToArray()
        }).ToList(),
        History = History.ToList(), Events = Events.ToList()
    };

    public static Agent Restore(SimulationSnapshot s, Dataset data)
    {
        if (data.Inputs != s.LayerSizes[0] || data.Classes != s.LayerSizes[^1])
            throw new InvalidOperationException($"This experiment used the '{s.DatasetName}' dataset. Load it first.");
        var a = new Agent(s.AgentId, data, Stages.ByName(s.StageName), Enum.Parse<RuleKind>(s.RuleName), s.LayerSizes[1], pretrain: false);
        for (int l = 1; l < a.Net.Layers.Length; l++)
        {
            var st = s.Layers[l - 1]; var ns = a.Net.Layers[l].Neurons;
            for (int i = 0; i < ns.Length; i++)
            {
                ns[i].W = st.W[i]; ns[i].Alive = st.Alive[i]; ns[i].Bias = st.Bias[i]; ns[i].Lesioned = st.Lesioned[i];
                ns[i].Pending = new double[ns[i].W.Length];
            }
        }
        a.Cycle = s.Cycle; a.LearningRate = s.LearningRate; a.ForgettingRate = s.ForgettingRate;
        a.History = s.History; a.Events = s.Events;
        return a;
    }
}
