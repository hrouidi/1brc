using System.Diagnostics;

namespace OneBrc.HRouidi
{
    internal class Program
    {
        static void Main(string[] args)
        {
            //const string filePath = @"C:\Users\WT6540\source\extern\1brc.hrouidi\DataGenerator\bin\Debug\net8.0\1b.measurements.txt";
            const string filePath = @"D:\Workspace\hrouidi\1brc\DataGenerator\bin\Debug\net8.0\1billion.measurements.txt";

            Console.WriteLine("Program started...");
            string path = args.Length > 0 ? args[0] : filePath;
            Stopwatch sw = Stopwatch.StartNew();
            using FileAggregator app = new(path);
            (TimeSpan processTimeSpan, TimeSpan sortAndPrintTimeSpan) = app.ProcessAndPrintResult();
            sw.Stop();
            Console.WriteLine($"====== Processed in {sw.Elapsed}");
            Console.WriteLine($"   ### Process time={processTimeSpan}");
            Console.WriteLine($"   ### Sort & print time ns ={sortAndPrintTimeSpan.TotalNanoseconds}");
            Console.ReadLine();
        }
    }
}
