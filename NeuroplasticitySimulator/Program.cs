using System;
using System.Collections.Generic;

class Program
{
    class Synapse
    {
        public Neuron inNeuron { get; private set; }
        public Neuron outNeuron { get; private set; }
        public double weight { get; set; }

        public double activity { get; set; } // Activation History
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
            if (isPruned)
            {
                return 0.0;
            }

            if (Math.Abs(inputVal) > 0.05)
            {
                idleSteps = 0.0;
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
        public List<Synapse> inSynapses { get; set; } = new List<Synapse>();
        public List<Synapse> outSynapses { get; set; } = new List<Synapse>();
        public Neuron()
        {
            Random rand = new Random();
            bias = (rand.NextDouble() * 0.2) - 0.1;
        }

        protected double SumWeights()
        {
            double sum = 0.0;

            foreach (Synapse synapse in inSynapses)
            {
                double preSignal = synapse.inNeuron.lastOutput;
                sum  += synapse.Fire(preSignal);
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
        private double Relu(double x)
        {
            return Math.Max(0.0, x);
        }

        public override double FPass()
        {
            double sum = SumWeights();  
            lastOutput = Relu(sum);   
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

        private double[] Softmax(double[] rawScores)
        {
            double[] probs = new double[rawScores.Length];
            double expSum = 0.0;
            for (int i = 0; i < rawScores.Length; i++)
            {
                expSum += Math.Exp(rawScores[i]);
            }

            for (int i = 0; i < rawScores.Length; i++)
            {
                probs[i] = Math.Exp(rawScores[i]) / expSum;
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
                    double initWeight = (rand.NextDouble() * 0.4) - 0.2;

                    Synapse synapse = new Synapse(srcNeuron, tgtNeuron, initWeight);

                    srcNeuron.outSynapses.Add(synapse);
                    tgtNeuron.inSynapses.Add(synapse);
                }
            }
        }
    }

    static void Main(string[] args)
    {
        double[] inputVector = { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0 };

        Layer inputLayer = new Layer(10, isOutput: false);
        Layer hiddenLayer1 = new Layer(4, isOutput: false);
        Layer outputLayer = new Layer(2, isOutput: true);

        NetBuilder.LinkLayers(inputLayer, hiddenLayer1);
        NetBuilder.LinkLayers(hiddenLayer1, outputLayer);

        for (int i = 0; i < inputVector.Length; i++)
        {
            inputLayer.neurons[i].SetVal(inputVector[i]);
        }

        hiddenLayer1.FPass();
        double[] finalProbs = outputLayer.FPass();

        Console.WriteLine("\n--- Output Probabilities ---");
        for (int i = 0; i < finalProbs.Length; i++)
        {
            Console.WriteLine($"Class {i}: {finalProbs[i]:P2}");
        }
    }
}