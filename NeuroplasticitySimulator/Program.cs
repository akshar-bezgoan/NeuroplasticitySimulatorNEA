using System;

class Neuron
{
    private double[] weights;
    private double bias;
    private double relu(double x)
    {
        return Math.Max(0, x);
    }
    private double softmax(double x)
    {
        return Math.Exp(x) / (1 + Math.Exp(x));
    }

    public Neuron(int rows, int cols, Func<double, double> activationFunction)
    {
        weights = new double[rows * cols];
        
        Console.WriteLine("Neuron created.");
    }
}