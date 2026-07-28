using System;
class Program
{
    class Neuron
    {
        protected double[] weights;
        protected double bias;

        public Neuron(int rows, int cols)
        {
            weights = new double[rows * cols];
            var rand = new Random();
            
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = rand.NextDouble();
            }
            bias = rand.NextDouble();

            Console.WriteLine("Neuron created.");
        }

        public void displayData()
        {
            for (int i = 0; i < weights.Length; i++)
            {
                Console.WriteLine(weights[i]);
            }

            Console.WriteLine(bias);
        }


    }

    class HiddenNeuron : Neuron
    {
        public HiddenNeuron(int rows, int cols) : base(rows, cols){}
        private double relu(double x)
        {
            return Math.Max(0, x);
        }
        
        public double fpass(double[] x)
        {
            if (x.Length != weights.Length)
            {
                return -1;
            } else
            {
                double output = 1;
                for (int i = 0; i < x.Length; i++)
                {
                    output += x[i] * weights[i];
                }

                output += bias;
                output = relu(output);

                return output; 

            }
        }
    }

    class OutputNeuron : Neuron
    {
        public OutputNeuron(int rows, int cols) : base(rows, cols){}
        private double[] softmax(double[] x)
        {
            double[] p = new double[x.Length];
            double sum = 0;
            for (int j = 0; j < x.Length; j++)
            {
                sum += Math.Exp(x[j]);
            }
            
            for (int j = 0; j < x.Length; j++)
            {
                p[j] = Math.Exp(x[j]) / sum;
            }

            return p;
        }

        public double[] fpass(double[] x)
        {
            double[] output = softmax(x);
            return output;
        }
    }

    static void Main(string[] args)
    {
        double[] x = {1,2,3,4,5,6,7,8,9,10};
        /*
        HiddenNeuron n1 = new HiddenNeuron(10, 1);
        n1.displayData();
        double output = n1.fpass(x);
        Console.WriteLine(output);
        */
        OutputNeuron n2 = new OutputNeuron(10,1);
        //n2.displayData();
        double[] p = n2.fpass(x);
        for (int i = 0; i < p.Length; i++)
        {
            Console.WriteLine(p[i]);
        }
    }
}
    