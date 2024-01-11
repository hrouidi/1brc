using NUnit.Framework;
using System;
using System.Buffers;
using System.Collections.Generic;
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
            string all = File.ReadAllText(path);
            var tmp = all.Split(';');
            var max = tmp.MaxBy(x=>x.Length).Length;
            using Solution app = new(path);
            app.ProcessChunk1(new Dictionary<Solution.Utf8Span, Solution.Summary>(),0, 1743);
        }

        [Test]
        public void ChunkifyTest()
        {
            long fileLength = 14_595_855_653L;
            const int alignAs = 32;
            List<(long start, long length)> actual = Solution.Chunkify(fileLength, alignAs, out int rem);

            Assert.That(actual.Count, Is.EqualTo(Environment.ProcessorCount));
            Assert.That(actual.Select(x => x.length).Sum() + rem, Is.EqualTo(fileLength));

            foreach ((long start, long length) in actual)
                Assert.That(start % alignAs, Is.EqualTo(0));

        }
    }
}