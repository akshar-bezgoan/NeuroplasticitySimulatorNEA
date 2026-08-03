using System;
using System.Collections.Generic;

class Program
{
    class Synapse
    {
        public Neuron inNeuron { get; private set; }
        public Neuron outNeuron { get; private set; }
        public double weight { get; set; }

        public double activity { get; set; }
        public double idleSteps { get; set; }

        public bool isPruned => Math.Abs(weight) < 0.001;

        public Synapse(Neuron inNeuron, Neuron outNeuron, double initialWeight)
        {
            this.inNeuron = inNeuron;
            this.outNeuron = outNeuron;
            weight = initialWeight;
            activity = 0.0;
            idleSteps = 0.0;
        }

        public double Fire(double inputVal)
        {
            if (isPruned) return 0.0;

            if (Math.Abs(inputVal) > 0.01)
            {
                idleSteps = 0.0;
                // Exponential moving average for recent activation activity
                activity = (0.9 * activity) + (0.1 * Math.Abs(inputVal));
            }
            else
            {
                idleSteps++;
            }

            return inputVal * weight;
        }
    }

    abstract class Neuron
    {
        public double bias { get; set; }
        public double lastOutput { get; protected set; }
        public double errorGrad { get; set; }

        public List<Synapse> inSynapses { get; set; } = new List<Synapse>();
        public List<Synapse> outSynapses { get; set; } = new List<Synapse>();

        public Neuron()
        {
            Random rand = new Random();
            bias = rand.NextDouble() * 0.1;
        }

        protected double SumWeights()
        {
            double sum = 0.0;
            foreach (Synapse synapse in inSynapses)
            {
                sum += synapse.Fire(synapse.inNeuron.lastOutput);
            }
            return sum + bias;
        }

        public abstract double FPass();

        public void SetVal(double val)
        {
            lastOutput = val;
        }
    }

    class HiddenNeuron : Neuron
    {
        public HiddenNeuron() : base() { }

        private double LeakyRelu(double x)
        {
            return x > 0.0 ? x : 0.01 * x;
        }

        public override double FPass()
        {
            lastOutput = LeakyRelu(SumWeights());
            return lastOutput;
        }
    }

    class OutputNeuron : Neuron
    {
        public double rawScore { get; private set; }

        public OutputNeuron() : base() { }

        public override double FPass()
        {
            rawScore = SumWeights();
            lastOutput = rawScore;
            return lastOutput;
        }

        public void SetProb(double probability)
        {
            lastOutput = probability;
        }
    }

    class Layer
    {
        public List<Neuron> neurons { get; private set; }
        public bool isOutput { get; private set; }

        public Layer(int numNeurons, bool isOutput = false)
        {
            this.isOutput = isOutput;
            neurons = new List<Neuron>();

            for (int i = 0; i < numNeurons; i++)
            {
                if (this.isOutput)
                    neurons.Add(new OutputNeuron());
                else
                    neurons.Add(new HiddenNeuron());
            }
        }

        public double[] FPass()
        {
            double[] outputs = new double[neurons.Count];

            for (int i = 0; i < neurons.Count; i++)
            {
                outputs[i] = neurons[i].FPass();
            }

            if (isOutput)
            {
                outputs = Softmax(outputs);
                for (int i = 0; i < neurons.Count; i++)
                {
                    ((OutputNeuron)neurons[i]).SetProb(outputs[i]);
                }
            }

            return outputs;
        }

        // Subtract maxScore to shift logits <= 0 and prevent exp() overflow (NaN)
        private double[] Softmax(double[] rawScores)
        {
            double[] probs = new double[rawScores.Length];

            double maxScore = rawScores[0];
            for (int i = 1; i < rawScores.Length; i++)
            {
                if (rawScores[i] > maxScore) maxScore = rawScores[i];
            }

            double expSum = 0.0;
            for (int i = 0; i < rawScores.Length; i++)
            {
                probs[i] = Math.Exp(rawScores[i] - maxScore);
                expSum += probs[i];
            }

            for (int i = 0; i < rawScores.Length; i++)
            {
                probs[i] /= expSum;
            }

            return probs;
        }
    }

    static class NetBuilder
    {
        public static void LinkLayers(Layer srcLayer, Layer tgtLayer)
        {
            Random rand = new Random();

            foreach (Neuron srcNeuron in srcLayer.neurons)
            {
                foreach (Neuron tgtNeuron in tgtLayer.neurons)
                {
                    double initWeight = (rand.NextDouble() * 0.3) + 0.05;
                    Synapse synapse = new Synapse(srcNeuron, tgtNeuron, initWeight);

                    srcNeuron.outSynapses.Add(synapse);
                    tgtNeuron.inSynapses.Add(synapse);
                }
            }
        }
    }

    class PlasticityEngine
    {
        public double hebbRate { get; set; } = 0.02;
        public double gradRate { get; set; } = 0.05;
        public double decayRate { get; set; } = 0.001;

        public double ComputeGradients(List<Layer> network, int targetClass)
        {
            Layer outLayer = network[network.Count - 1];
            Layer hiddenLayer = network[network.Count - 2];

            double targetProb = outLayer.neurons[targetClass].lastOutput;
            double loss = -Math.Log(Math.Max(targetProb, 1e-15));

            // Output error gradient for Softmax + Cross-Entropy simplifies to (y_hat - y)
            for (int i = 0; i < outLayer.neurons.Count; i++)
            {
                double targetVal = (i == targetClass) ? 1.0 : 0.0;
                outLayer.neurons[i].errorGrad = outLayer.neurons[i].lastOutput - targetVal;
            }

            // Propagate error backward to hidden layer scaled by Leaky ReLU derivative
            for (int i = 0; i < hiddenLayer.neurons.Count; i++)
            {
                Neuron hNeuron = hiddenLayer.neurons[i];
                double sumGrad = 0.0;

                foreach (Synapse synapse in hNeuron.outSynapses)
                {
                    sumGrad += synapse.weight * synapse.outNeuron.errorGrad;
                }

                double derivative = hNeuron.lastOutput > 0.0 ? 1.0 : 0.01;
                hNeuron.errorGrad = sumGrad * derivative;
            }

            return loss;
        }

        public void ApplyHybridUpdate(List<Layer> network, int targetClass, int predClass)
        {
            bool accurate = (targetClass == predClass);

            foreach (Layer layer in network)
            {
                for (int nIdx = 0; nIdx < layer.neurons.Count; nIdx++)
                {
                    Neuron neuron = layer.neurons[nIdx];
                    neuron.bias -= gradRate * neuron.errorGrad;

                    foreach (Synapse synapse in neuron.inSynapses)
                    {
                        if (synapse.idleSteps > 3)
                        {
                            synapse.weight -= decayRate * synapse.idleSteps;
                        }

                        double preVal = synapse.inNeuron.lastOutput;
                        double gdUpdate = -gradRate * neuron.errorGrad * preVal;

                        double hebbUpdate = 0.0;
                        if (layer.isOutput)
                        {
                            // Target-specific Output Hebbian update prevents 50/50 equilibrium flatlining
                            hebbUpdate = (nIdx == targetClass) ? (hebbRate * synapse.activity) : (-hebbRate * synapse.activity);
                        }
                        else if (synapse.activity > 0.01)
                        {
                            hebbUpdate = accurate ? (hebbRate * synapse.activity) : (-hebbRate * synapse.activity);
                        }

                        synapse.weight += gdUpdate + hebbUpdate;
                        synapse.weight = Math.Clamp(synapse.weight, -3.0, 3.0);
                    }
                }
            }
        }
    }

    static void Main(string[] args)
    {
        double[] inputVector = { 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0 };
        int targetClass = 1;

        Layer layeri = new Layer(10, isOutput: false);
        Layer layerh1 = new Layer(4, isOutput: false);
        Layer layero = new Layer(2, isOutput: true);

        NetBuilder.LinkLayers(layeri, layerh1);
        NetBuilder.LinkLayers(layerh1, layero);

        List<Layer> network = new List<Layer> { layeri, layerh1, layero };
        PlasticityEngine engine = new PlasticityEngine();

        Console.WriteLine("--- Starting Hybrid Plasticity & Gradient Descent Simulation ---\n");

        for (int step = 1; step <= 1000; step++)
        {
            for (int i = 0; i < inputVector.Length; i++)
            {
                layeri.neurons[i].SetVal(inputVector[i]);
            }

            layerh1.FPass();
            double[] finalProbs = layero.FPass();

            int predClass = finalProbs[0] > finalProbs[1] ? 0 : 1;
            double loss = engine.ComputeGradients(network, targetClass);
            engine.ApplyHybridUpdate(network, targetClass, predClass);

            if (step == 1 || step % 100 == 0 || step == 1000)
            {
                Console.WriteLine($"Step {step:D4} | Pred: {predClass} | Target (Class {targetClass}) Prob: {finalProbs[targetClass]:P2} | Loss: {loss:F4}");
            }
        }

        int activeSynapses = 0;
        int prunedSynapses = 0;

        foreach (Layer layer in network)
        {
            foreach (Neuron neuron in layer.neurons)
            {
                foreach (Synapse synapse in neuron.inSynapses)
                {
                    if (synapse.isPruned) prunedSynapses++;
                    else activeSynapses++;
                }
            }
        }

        Console.WriteLine("\n--- Structural Circuit Pruning Results ---");
        Console.WriteLine($"Active Functional Connections : {activeSynapses}");
        Console.WriteLine($"Pruned Inactive Connections  : {prunedSynapses}");
    }
}