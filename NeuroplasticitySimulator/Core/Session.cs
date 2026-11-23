using System.Diagnostics;
using Avalonia.Media;

namespace Neuroplasticity.Core;

/// <summary>One experiment: a dataset plus one or more agents that train side by side.</summary>
public class SimulationSession
{
    public static readonly Color[] Palette =
    {
        Color.Parse("#63BFC3"), Color.Parse("#829DD1"), Color.Parse("#D9A958"),
        Color.Parse("#75C79A"), Color.Parse("#C9848E"), Color.Parse("#97AEB2")
    };
    public const int MaxAgents = 6;

    public long? DbId;
    public string Name = "Experiment";
    public Dataset Data;
    public string DataKind = "digits", DataPath;
    public List<Agent> Agents = new();
    public bool Running;
    public string Status = "Paused";        // Running | Paused | Completed
    public TimeSpan Elapsed;
    public int TargetCycles = 5000;
    public int Speed = 5;                   // learning cycles per screen refresh

    public int Cycle => Agents.Count == 0 ? 0 : Agents.Max(a => a.Cycle);
    public int Progress => Math.Min(100, (int)(Cycle * 100L / Math.Max(1, TargetCycles)));
    public bool Finished => Agents.Count > 0 && Agents.All(a => a.Cycle >= TargetCycles);
    public string Architecture => Agents.Count == 0 ? "" : $"{Data.Inputs}-{Agents[0].Net.Layers[1].Neurons.Length}-{Data.Classes}";
    public Color ColourOf(Agent a) => Palette[Math.Max(0, Agents.IndexOf(a)) % Palette.Length];

    public Agent AddAgent(DevelopmentStage stage, RuleKind rule, int hidden = 24)
    {
        var a = new Agent(NextId(), Data, stage, rule, hidden);
        Agents.Add(a);
        return a;
    }

    string NextId()
    {
        for (char c = 'A'; c <= 'Z'; c++) if (Agents.All(a => a.Id != c.ToString())) return c.ToString();
        return Agents.Count.ToString();
    }

    public void Step(int cycles, int budgetMs)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < cycles && sw.ElapsedMilliseconds < budgetMs; i++)
        {
            bool any = false;
            foreach (var a in Agents) if (a.Cycle < TargetCycles) { a.Step(); any = true; }
            if (!any) break;
        }
    }
}
