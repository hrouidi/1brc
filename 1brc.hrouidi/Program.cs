using System.Diagnostics;

namespace _1brc.hrouidi
{
    internal class Program
    {
        static void Main(string[] args)
        {
            //const string filePath = @"C:\Users\WT6540\source\extern\1brc.hrouidi\DataGenerator\bin\Debug\net8.0\1b.measurements.txt";
            const string filePath = @"D:\Workspace\hrouidi\1brc\DataGenerator\bin\Debug\net8.0\1B.measurements.txt";

            Console.WriteLine("Program started...");
            string path = args.Length > 0 ? args[0] : filePath;
            Stopwatch sw = Stopwatch.StartNew();
            using Solution app = new(path);
            app.PrintResult();
            sw.Stop();
            Console.WriteLine($"Processed in {sw.Elapsed}");
            Console.ReadLine();
        }
    }
}
