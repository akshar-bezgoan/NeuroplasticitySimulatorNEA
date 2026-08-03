using System;
using System.Collections.Generic;
using System.IO;

class Program
{
    // Mode selector for testing different engine strategies
    public enum LearningMode
    {
        BiologicalOja,        // 1. Pure Bio: Oja's rule + Reward-gated Hebbian
        PureGradientDescent,  // 2. Pure ML: Cross-Entropy Backprop + Gradient Descent
        Hybrid                // 3. Hybrid: Backprop + Targeted Hebbian + Decay
    }

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
                // Exponential moving average for activation activity
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

    // ==========================================
    // REFACTORED PLASTICITY ENGINE
    // ==========================================
    class PlasticityEngine
    {
        public double hebbRate { get; set; } = 0.01;
        public double gradRate { get; set; } = 0.03;
        public double decayRate { get; set; } = 0.0005;

        // Computes Cross-Entropy loss & error gradients via backpropagation
        public double ComputeGradients(List<Layer> network, int targetClass)
        {
            Layer outLayer = network[network.Count - 1];
            Layer hiddenLayer = network[network.Count - 2];

            double targetProb = outLayer.neurons[targetClass].lastOutput;
            double loss = -Math.Log(Math.Max(targetProb, 1e-15));

            // Output error gradient for Softmax + Cross-Entropy: (y_hat - y)
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

        // ---------------------------------------------------------------------
        // 1. PURE BIOLOGICAL: Oja's Normalized Rule + Reward-Gated Hebbian
        // ---------------------------------------------------------------------
        public void ApplyBiologicalUpdate(List<Layer> network, int targetClass, int predClass)
        {
            bool accurate = (targetClass == predClass);

            foreach (Layer layer in network)
            {
                for (int nIdx = 0; nIdx < layer.neurons.Count; nIdx++)
                {
                    Neuron neuron = layer.neurons[nIdx];

                    foreach (Synapse synapse in neuron.inSynapses)
                    {
                        if (synapse.isPruned) continue;

                        // Passive decay for inactive connections
                        if (synapse.idleSteps > 3)
                        {
                            synapse.weight -= decayRate * synapse.idleSteps;
                        }

                        double preAct = synapse.inNeuron.lastOutput;
                        double postAct = neuron.lastOutput;

                        if (layer.isOutput)
                        {
                            // Output layer targeted gating
                            if (nIdx == targetClass)
                            {
                                // Oja's self-stabilizing rule: dw = eta * pre * (post - post^2 * w)
                                double delta = hebbRate * preAct * (postAct - (postAct * postAct * synapse.weight));
                                synapse.weight += delta;
                            }
                            else
                            {
                                synapse.weight -= hebbRate * synapse.activity;
                            }
                        }
                        else if (synapse.activity > 0.01)
                        {
                            // Hidden layer co-activation reinforcement
                            if (accurate)
                            {
                                double delta = hebbRate * preAct * (postAct - (postAct * postAct * synapse.weight));
                                synapse.weight += delta;
                            }
                            else
                            {
                                synapse.weight -= hebbRate * synapse.activity;
                            }
                        }

                        synapse.weight = Math.Clamp(synapse.weight, -3.0, 3.0);
                    }
                }
            }
        }

        // ---------------------------------------------------------------------
        // 2. PURE ML: Standard Backpropagation & Gradient Descent
        // ---------------------------------------------------------------------
        public void ApplyGradientDescentUpdate(List<Layer> network)
        {
            foreach (Layer layer in network)
            {
                foreach (Neuron neuron in layer.neurons)
                {
                    // Update bias: b = b - gradRate * errorGrad
                    neuron.bias -= gradRate * neuron.errorGrad;

                    foreach (Synapse synapse in neuron.inSynapses)
                    {
                        if (synapse.isPruned) continue;

                        double preVal = synapse.inNeuron.lastOutput;

                        // Update weight: w = w - gradRate * errorGrad * preInput
                        double gdUpdate = -gradRate * neuron.errorGrad * preVal;
                        synapse.weight += gdUpdate;

                        synapse.weight = Math.Clamp(synapse.weight, -3.0, 3.0);
                    }
                }
            }
        }

        // ---------------------------------------------------------------------
        // 3. HYBRID: Combined Gradient Descent + Targeted Hebbian + Decay
        // ---------------------------------------------------------------------
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
                        if (synapse.isPruned) continue;

                        // A. Passive Decay
                        if (synapse.idleSteps > 3)
                        {
                            synapse.weight -= decayRate * synapse.idleSteps;
                        }

                        // B. Gradient Descent component
                        double preVal = synapse.inNeuron.lastOutput;
                        double gdUpdate = -gradRate * neuron.errorGrad * preVal;

                        // C. Hebbian component
                        double hebbUpdate = 0.0;
                        if (layer.isOutput)
                        {
                            hebbUpdate = (nIdx == targetClass) ? (hebbRate * synapse.activity) : (-hebbRate * synapse.activity);
                        }
                        else if (accurate && synapse.activity > 0.01)
                        {
                            hebbUpdate = hebbRate * synapse.activity;
                        }

                        // Combine global loss reduction with local pathway reinforcement
                        synapse.weight += gdUpdate + hebbUpdate;
                        synapse.weight = Math.Clamp(synapse.weight, -3.0, 3.0);
                    }
                }
            }
        }

        // Dispatcher method to trigger specified mode
        public double ExecuteStep(List<Layer> network, int targetClass, int predClass, LearningMode mode)
        {
            double loss = 0.0;

            if (mode == LearningMode.PureGradientDescent || mode == LearningMode.Hybrid)
            {
                loss = ComputeGradients(network, targetClass);
            }

            switch (mode)
            {
                case LearningMode.BiologicalOja:
                    ApplyBiologicalUpdate(network, targetClass, predClass);
                    break;
                case LearningMode.PureGradientDescent:
                    ApplyGradientDescentUpdate(network);
                    break;
                case LearningMode.Hybrid:
                    ApplyHybridUpdate(network, targetClass, predClass);
                    break;
            }

            return loss;
        }
    }

    class MnistLoader
    {
        public struct Sample
        {
            public double[] pixels;
            public int label;
        }

        public static List<Sample> Load(string imagePath, string labelPath, int maxSamples = 10000)
        {
            List<Sample> dataset = new List<Sample>();

            using FileStream imgStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
            using FileStream lblStream = new FileStream(labelPath, FileMode.Open, FileAccess.Read);
            using BinaryReader imgReader = new BinaryReader(imgStream);
            using BinaryReader lblReader = new BinaryReader(lblStream);

            int imgMagic = ReadBigEndianInt32(imgReader);
            int totalImages = ReadBigEndianInt32(imgReader);
            int rows = ReadBigEndianInt32(imgReader);
            int cols = ReadBigEndianInt32(imgReader);

            int lblMagic = ReadBigEndianInt32(lblReader);
            int totalLabels = ReadBigEndianInt32(lblReader);

            int count = Math.Min(maxSamples, totalImages);
            int imageSize = rows * cols;

            for (int i = 0; i < count; i++)
            {
                byte label = lblReader.ReadByte();
                byte[] rawPixels = imgReader.ReadBytes(imageSize);

                double[] normalizedPixels = new double[imageSize];
                for (int p = 0; p < imageSize; p++)
                {
                    normalizedPixels[p] = rawPixels[p] / 255.0;
                }

                dataset.Add(new Sample { pixels = normalizedPixels, label = label });
            }

            return dataset;
        }

        private static int ReadBigEndianInt32(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }
    }

    static void Main(string[] args)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        string trainImagesPath = Path.Combine(baseDir, "train-images-idx3-ubyte");
        string trainLabelsPath = Path.Combine(baseDir, "train-labels-idx1-ubyte");

        if (!File.Exists(trainImagesPath) || !File.Exists(trainLabelsPath))
        {
            Console.WriteLine($"Please place 'train-images-idx3-ubyte' and 'train-labels-idx1-ubyte' in your output directory ({baseDir}).");
            return;
        }

        Console.WriteLine("Loading MNIST dataset into memory...");
        List<MnistLoader.Sample> dataset = MnistLoader.Load(trainImagesPath, trainLabelsPath, maxSamples: 5000);
        Console.WriteLine($"Loaded {dataset.Count} MNIST samples.\n");

        // Select learning mode to execute: BiologicalOja | PureGradientDescent | Hybrid
        LearningMode selectedMode = LearningMode.Hybrid;

        PlasticityEngine engine = new PlasticityEngine
        {
            hebbRate = 0.001,
            gradRate = 0.005,
            decayRate = 0.0002
        };

        /*
        LearningMode selectedMode = LearningMode.PureGradientDescent;

        PlasticityEngine engine = new PlasticityEngine
        {
            gradRate = 0.01     // Clean, classic backpropagation rate
        };
        */

        /*LearningMode selectedMode = LearningMode.BiologicalOja;

        PlasticityEngine engine = new PlasticityEngine
        {
            hebbRate = 0.005,
            decayRate = 0.0001
        };*/

        Layer layeri = new Layer(784, isOutput: false);
        Layer layerh1 = new Layer(64, isOutput: false);
        Layer layero = new Layer(10, isOutput: true);

        NetBuilder.LinkLayers(layeri, layerh1);
        NetBuilder.LinkLayers(layerh1, layero);

        List<Layer> network = new List<Layer> { layeri, layerh1, layero };

        Console.WriteLine($"--- Starting MNIST Training Loop [Mode: {selectedMode}] ---\n");

        Random rng = new Random();
        int correctPredictions = 0;

        for (int step = 1; step <= dataset.Count; step++)
        {
            MnistLoader.Sample sample = dataset[rng.Next(dataset.Count)];

            for (int i = 0; i < sample.pixels.Length; i++)
            {
                layeri.neurons[i].SetVal(sample.pixels[i]);
            }

            layerh1.FPass();
            double[] finalProbs = layero.FPass();

            int predClass = 0;
            double maxProb = finalProbs[0];
            for (int c = 1; c < finalProbs.Length; c++)
            {
                if (finalProbs[c] > maxProb)
                {
                    maxProb = finalProbs[c];
                    predClass = c;
                }
            }

            if (predClass == sample.label) correctPredictions++;

            // Execute update based on selected LearningMode
            double loss = engine.ExecuteStep(network, sample.label, predClass, selectedMode);

            if (step % 200 == 0)
            {
                double accuracy = (double)correctPredictions / 200.0 * 100.0;
                Console.WriteLine($"Step {step:D5} | Sample Label: {sample.label} | Pred: {predClass} | Target Prob: {finalProbs[sample.label]:P2} | Recent Acc: {accuracy:F1}% | Loss: {loss:F4}");
                correctPredictions = 0;
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