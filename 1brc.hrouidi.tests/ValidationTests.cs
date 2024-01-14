using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OneBrc.HRouidi.tests
{
    public class ValidationTests
    {
        public class Samples : IEnumerable
        {
            public IEnumerator GetEnumerator()
            {
                return Directory.EnumerateFiles(@".\Samples").GroupBy(Path.GetFileNameWithoutExtension)
                                .Select(x => x.OrderByDescending(xx => xx).ToArray())
                                .GetEnumerator();
            }
        }


        [Test]
        [TestCaseSource(typeof(Samples))]
        public void ProcessChunkTest(string inputFilePath, string expectedFilePath)
        {
            using FileAggregator aggregator = new(inputFilePath);
            Dictionary<Utf8Span, Statistics> output = aggregator.Process();
            string actual = FileAggregator.SortAndDumpToString(output);
            string expected = File.ReadAllText(expectedFilePath).TrimEnd();
            Assert.That(actual, Is.EqualTo(expected));
        }
    }
}