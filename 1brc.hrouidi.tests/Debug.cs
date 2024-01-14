using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using _1brc;
using NUnit.Framework;
using NUnit.Framework.Constraints;

namespace OneBrc.HRouidi.tests
{
    public class Debug
    {
        public readonly struct StationSpan(string bytes, HashSet<(string, string)> collisionSet) : IEquatable<StationSpan>
        {
            private readonly string _bytes = bytes;

            public bool Equals(StationSpan other)
            {
                var ret = _bytes.Equals(other._bytes);

                if (ret == false)
                    collisionSet.Add((ToString(), other.ToString()));

                return ret;
            }

            public override bool Equals(object? obj) => obj is StationSpan other && Equals(other);

            public override int GetHashCode()
            {
                return _bytes.GetHashCode();
            }

            public override string ToString()
            {
                return _bytes.ToString();
            }
        }

        [Test]
        public void Test1_origin()
        {
            //const string path = @"C:\Users\WT6540\source\extern\1brc.hrouidi\DataGenerator\bin\Debug\net8.0\120.measurements.txt";
            const string path = @"D:\Workspace\hrouidi\1brc\DataGenerator\bin\Debug\net8.0\1B.measurements.txt";
            using App app = new(path);
            //long length= RandomAccess.GetLength(File.OpenHandle(path, FileMode.Open,FileAccess.Read,FileShare.Read,FileOptions.None));
            //Dictionary<FileAggregator.Utf8Span, FileAggregator.Statistics> dic = new();
            Dictionary<_1brc.Utf8Span, Summary> ret = app.ProcessChunk(0, 1_785);
        }


        [Test]
        public void Test1()
        {
            //const string path = @"C:\Users\WT6540\source\extern\1brc.hrouidi\DataGenerator\bin\Debug\net8.0\120.measurements.txt";
            const string path = @"D:\Workspace\hrouidi\1brc\DataGenerator\bin\Debug\net8.0\120.measurements.txt";
            using FileAggregator aggregator = new(path);
            Dictionary<Utf8Span, Statistics> dic = new();
            aggregator.ProcessChunk(dic, 0, (int)aggregator.Mmf.FileLength);
            //app.ProcessAndPrintResult();
        }

        [Test]
        [TestCaseSource(typeof(Samples))]
        public void ProcessChunkTest(string inputFilePath, string expectedFilePath)
        {
            using FileAggregator aggregator = new(inputFilePath);
            Dictionary<Utf8Span, Statistics> output = aggregator.Process();
            var actual = FileAggregator.SortAndDumpToString(output);
            var expected = File.ReadAllText(expectedFilePath).TrimEnd();
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void ChunkifyTest()
        {
            //14 _795_ 970_ 091 engie
            long fileLength = 14_595_855_653L;
            const int alignAs = 32;
            List<(long start, long length)> actual = FileAggregator.GenerateAlignedChunks(fileLength, alignAs, out int rem);

            Assert.That(actual.Count, Is.EqualTo(Environment.ProcessorCount));
            Assert.That(actual.Select(x => x.length).Sum() + rem, Is.EqualTo(fileLength));

            foreach ((long start, long length) in actual)
                Assert.That(start % alignAs, Is.EqualTo(0));

        }

        public class Samples : IEnumerable
        {
            public IEnumerator GetEnumerator()
            {
                var tmp = Directory.EnumerateFiles(@".\Samples");
                var ret = tmp.GroupBy(Path.GetFileNameWithoutExtension)
                             .Select(x => x.OrderByDescending(x=>x).ToArray());
                return ret.GetEnumerator();
            }
        }
    }
}