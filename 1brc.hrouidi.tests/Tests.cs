using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Program = DataGenerator.Program;

namespace _1brc.hrouidi.tests
{
    public class Tests
    {

        [Test]
        public void Test1()
        {
            //const string path = @"C:\Users\WT6540\source\extern\1brc.hrouidi\DataGenerator\bin\Debug\net8.0\120.measurements.txt";
            const string path = @"D:\Workspace\hrouidi\1brc\DataGenerator\bin\Debug\net8.0\1B.measurements.txt";
            using Aggregator app = new(path);
            Dictionary<Utf8Span, Statistics> dic = new();
            app.ProcessChunk1(dic, 0, 1743);
        }

        [Test]
        public void Test1_origin()
        {
            //const string path = @"C:\Users\WT6540\source\extern\1brc.hrouidi\DataGenerator\bin\Debug\net8.0\120.measurements.txt";
            const string path = @"D:\Workspace\hrouidi\1brc\DataGenerator\bin\Debug\net8.0\1B.measurements.txt";
            using App app = new(path);
            //long length= RandomAccess.GetLength(File.OpenHandle(path, FileMode.Open,FileAccess.Read,FileShare.Read,FileOptions.None));
            //Dictionary<Aggregator.Utf8Span, Aggregator.Statistics> dic = new();
            Dictionary<_1brc.Utf8Span, Summary> ret = app.ProcessChunk(0, 1_785);
        }

        [Test]
        public void ChunkifyTest()
        {
            //14 _795_ 970_ 091 engie
            long fileLength = 14_595_855_653L;
            const int alignAs = 32;
            List<(long start, long length)> actual = Aggregator.Chunkify(fileLength, alignAs, out int rem);

            Assert.That(actual.Count, Is.EqualTo(Environment.ProcessorCount));
            Assert.That(actual.Select(x => x.length).Sum() + rem, Is.EqualTo(fileLength));

            foreach ((long start, long length) in actual)
                Assert.That(start % alignAs, Is.EqualTo(0));

        }

        [Test]
        public unsafe void StationNameHashCollisionDebug()
        {
            var tmp = Program.Stations.OrderBy(x => x.Id.Length)
                             .Select(x => (x.Id, length: x.Id.Length))
                             .GroupBy(x => x.length)
                             .Select(x => (length: x.Key, count: x.Count()))
                             .ToArray();

            var tmp2 = tmp.OrderBy(x => x.count)
                          .ToArray();

            HashSet<(string, string)> collisionSet = new();

            var stationsNamesUtf8 = Program.Stations
                                           .Select(x => Encoding.UTF8.GetBytes(x.Id))
                                           .ToArray();

            var dic1 = Program.Stations.Select(x => new StationSpan(x.Id, collisionSet))
                              .ToDictionary(x => x);

            collisionSet.Clear();

            var dic2 = stationsNamesUtf8.Select(x => new Utf8Span((byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(x)),x.Length, collisionSet))
                                        .ToDictionary(x => x);
        }
    }

    public readonly struct StationSpan(string bytes, HashSet<(string, string)> collisionSet) : IEquatable<StationSpan>
    {
        private readonly string _bytes = bytes;

        public bool Equals(StationSpan other)
        {
            var ret = _bytes.Equals(other._bytes);

            if (ret == false)
                collisionSet.Add((this.ToString(), other.ToString()));

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
}