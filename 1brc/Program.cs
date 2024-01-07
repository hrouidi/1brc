using System.Diagnostics;

namespace _1brc;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Program started...");
        var sw = Stopwatch.StartNew();
        var path = args.Length > 0 ? args[0] : @"D:\Workspace\hrouidi\1brc\DataGenerator\bin\Debug\net8.0\measurements.txt";
        {
            using var app = new App(path);

            app.PrintResult();
            sw.Stop();
        }

        Console.ReadLine();
    }
}