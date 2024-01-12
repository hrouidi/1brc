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

        [Test]
        public void Test1()
        {
            //const string path = @"C:\Users\WT6540\source\extern\1brc.hrouidi\DataGenerator\bin\Debug\net8.0\120.measurements.txt";
            const string path = @"D:\Workspace\hrouidi\1brc\DataGenerator\bin\Debug\net8.0\1B.measurements.txt";
            using Solution app = new(path);
            Dictionary<Solution.Utf8Span, Solution.Summary> dic = new();
            app.ProcessChunk1(dic, 0, 1743);
        }

        [Test]
        public void Test1_origin()
        {
            //const string path = @"C:\Users\WT6540\source\extern\1brc.hrouidi\DataGenerator\bin\Debug\net8.0\120.measurements.txt";
            const string path = @"D:\Workspace\hrouidi\1brc\DataGenerator\bin\Debug\net8.0\1B.measurements.txt";
            using App app = new(path);
            //long length= RandomAccess.GetLength(File.OpenHandle(path, FileMode.Open,FileAccess.Read,FileShare.Read,FileOptions.None));
            //Dictionary<Solution.Utf8Span, Solution.Summary> dic = new();
            Dictionary<Utf8Span, Summary> ret = app.ProcessChunk( 0, 1_785);
        }

        [Test]
        public void ChunkifyTest()
        {
            //14 _795_ 970_ 091 engie
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