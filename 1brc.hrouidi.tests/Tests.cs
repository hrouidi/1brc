using NUnit.Framework;
using System;
using System.Buffers;
using System.IO;
using System.Linq;

namespace _1brc.hrouidi.tests
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void Test1()
        {
           
            const string path = @"D:\Workspace\hrouidi\1brc\DataGenerator\bin\Debug\net8.0\120.measurements.txt";
            using Solution app = new(path);
            app.ProcessChunk(0,1743);
        }
    }
}