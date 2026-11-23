using System.IO.Compression;
using System.Text.Json;

namespace Neuroplasticity.Core;

public class LayerState
{
    public double[][] W { get; set; }
    public bool[][] Alive { get; set; }
    public double[] Bias { get; set; }
    public bool[] Lesioned { get; set; }
}

/// <summary>Everything needed to resume an agent exactly where it was paused (spec 2.6.3.1).</summary>
public class SimulationSnapshot
{
    public string AgentId { get; set; }
    public string StageName { get; set; }
    public string RuleName { get; set; }
    public string DatasetName { get; set; }
    public int Cycle { get; set; }
    public double LearningRate { get; set; }
    public double ForgettingRate { get; set; }
    public int[] LayerSizes { get; set; }
    public List<LayerState> Layers { get; set; }
    public List<MetricPoint> History { get; set; }
    public List<SimEvent> Events { get; set; }
}

public static class SimulationSerialiser
{
    public static byte[] Serialise(SimulationSnapshot s)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, true)) JsonSerializer.Serialize(gz, s);
        return ms.ToArray();
    }

    public static SimulationSnapshot Deserialise(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<SimulationSnapshot>(gz);
    }
}
