using System.Diagnostics;

namespace _1brc.hrouidi
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Program started...");
            string path = args.Length > 0 ? args[0] : @"D:\Workspace\hrouidi\1brc\DataGenerator\bin\Debug\net8.0\measurements.txt";
            Stopwatch sw = Stopwatch.StartNew();
            using (Solution app = new(path))
            {
                app.PrintResult();
                sw.Stop();
            }
            Console.ReadLine();
        }
    }
}
