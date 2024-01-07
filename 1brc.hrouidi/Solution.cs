using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using _1brc.hrouidi;

namespace _1brc
{
    public class Solution : IDisposable
    {
        private readonly FileStream _fileStream;
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _va;

        private readonly long _fileLength;
        private readonly int _initialChunkCount;

        
        public Solution(string filePath)
        {
            _initialChunkCount = Environment.ProcessorCount;
            _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
            var fileLength = _fileStream.Length;
            _mmf = MemoryMappedFile.CreateFromFile(_fileStream, $"{Path.GetFileName(filePath)}", fileLength, MemoryMappedFileAccess.Read, HandleInheritability.None, true);
            _va = _mmf.CreateViewAccessor(0, fileLength, MemoryMappedFileAccess.Read);

            _fileLength = fileLength;
        }

        public List<(long start, int length)> SplitIntoMemoryChunks()
        {

            // We want equal chunks not larger than int.MaxValue
            // We want the number of chunks to be a multiple of CPU count, so multiply by 2
            // Otherwise with CPU_N+1 chunks the last chunk will be processed alone.

            const int maxChunkSize = int.MaxValue - 100_000;

            int chunkCount = _initialChunkCount;
            long chunkSize = _fileLength / chunkCount;
            while (chunkSize > maxChunkSize)
            {
                chunkCount *= 2;
                chunkSize = _fileLength / chunkCount;
            }

            List<(long start, int length)> chunks = new(chunkCount);

            long pos = 0;

            for (int i = 0; i < chunkCount; i++)
            {
                if (pos + chunkSize >= _fileLength)
                {
                    chunks.Add((pos, (int)(_fileLength - pos)));
                    break;
                }

                long newPos = pos + chunkSize;
                //ReadOnlySpan<byte> sp = new ReadOnlySpan<byte>(_pointer + newPos, (int)chunkSize);
                ReadOnlySpan<byte> sp = _va.AsSpan(newPos, (int)chunkSize);
                //var idx = IndexOfNewlineChar(sp, out var stride);
                int idx = Helpers.IndexOfNewline(sp);
                newPos += idx + Helpers.NewLineBytesCount;
                long len = newPos - pos;
                chunks.Add((pos, (int)len));
                pos = newPos;
            }

            return chunks;
        }

        public Dictionary<Utf8Span, Summary> ProcessChunk0(long start, int length)
        {
            Dictionary<Utf8Span, Summary> result = new(1024);

            ReadOnlySpan<byte> span = _va.AsSpan(start, length);
            int pos = 0;

            while (pos < length)
            {
                long offset = start + pos;
                ReadOnlySpan<byte> sp = span.Slice(pos, length);

                int sepIdx = sp.IndexOf((byte)';');

                ref Summary summary = ref CollectionsMarshal.GetValueRefOrAddDefault(result, new Utf8Span(_va, offset, sepIdx), out bool _);

                double value = DoubleParser.ParseNaive(sp[++sepIdx..], out int bytes); 

                summary.Apply(value);

                pos += sepIdx + bytes + Helpers.NewLineBytesCount;
            }

            return result;
        }

        public Dictionary<Utf8Span, Summary> ProcessChunk(long start, int length)
        {
            Dictionary<Utf8Span, Summary> result = new(512);

            ReadOnlySpan<byte> span = _va.AsSpan(start, length);
            int spanCurrentPosition = 0;
            while (spanCurrentPosition < length)
            {
                ReadOnlySpan<byte> sp = span.Slice(spanCurrentPosition);

                int sepIdx = sp.IndexOf((byte)';');
                double value = DoubleParser.ParseNaive(sp.Slice(++sepIdx), out int bytes);

                ref Summary summary = ref CollectionsMarshal.GetValueRefOrAddDefault(result, new Utf8Span(_va, start + spanCurrentPosition, sepIdx), out bool _);
                summary.Apply(value);

                spanCurrentPosition += sepIdx + bytes + Helpers.NewLineBytesCount;
            }

            return result;
        }

        public Dictionary<Utf8Span, Summary> Process()
        {

            List<(long start, int length)> chunkRanges = SplitIntoMemoryChunks();


            List<Dictionary<Utf8Span, Summary>> chunks = chunkRanges.AsParallel()
                                                                    .Select((tuple => ProcessChunk(tuple.start, tuple.length)))
                                                                    .ToList();


            Dictionary<Utf8Span, Summary>? result = chunks[0];

            foreach (Dictionary<Utf8Span, Summary> chunk in chunks[1..])
            {
                foreach (KeyValuePair<Utf8Span, Summary> pair in chunk)
                {
                    ref Summary summary = ref CollectionsMarshal.GetValueRefOrAddDefault(result, pair.Key, out bool exists);
                    if (exists)
                        summary.Apply(pair.Value);
                    else
                        summary = pair.Value;
                }
            }

            return result!;
        }

        public void PrintResult()
        {
            var sw = Stopwatch.StartNew();
            Dictionary<Utf8Span, Summary> result = Process();

            foreach ((Utf8Span key, Summary value) in result.OrderBy(x => x.Key.ToString()))
                //foreach ((Utf8Span key, Summary value) in result)
                Console.WriteLine($"{key} = {value}");

            sw.Stop();
            Console.WriteLine($"Processed in {sw.Elapsed}");
        }

        public void Dispose()
        {
            _va.Dispose();
            _mmf.Dispose();
            _fileStream.Dispose();
        }

        public struct Summary
        {
            public double Min;
            public double Max;
            public double Sum;
            public long Count;

            public Summary()
            {
                Min = float.MaxValue;
                Max = float.MinValue;
                Sum = 0;
                Count = 0;
            }

            public double Average => Sum / Count;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Apply(double value)
            {
                //if (value < Min)
                //    Min = value;
                //else if (value > Max)
                //    Max = value;
                Min = Math.Min(Min, value);
                Max = Math.Max(Max, value);
                Sum += value;
                Count++;
            }

            public void Apply(Summary other)
            {
                if (other.Min < Min)
                    Min = other.Min;
                if (other.Max > Max)
                    Max = other.Max;
                Sum += other.Sum;
                Count += other.Count;
            }

            public override string ToString() => $"{Min:N2}/{Average:N2}/{Max:N2}";
        }

        public readonly struct Utf8Span(MemoryMappedViewAccessor accessor, long offset, int length) : IEquatable<Utf8Span>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private ReadOnlySpan<byte> GetSpan() => accessor.AsSpan(offset, length);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(Utf8Span other) => GetSpan().SequenceEqual(other.GetSpan());

            public override bool Equals(object? obj) => obj is Utf8Span other && Equals(other);

            public override int GetHashCode()
            {
                // Here we use the first 4 chars (if ASCII) and the length for a hash.
                // The worst case would be a prefix such as Port/Saint and the same length,
                // which for human geo names is quite rare. 

                // .NET dictionary will obviously slow down with collisions but will still work.
                // If we keep only `*_pointer` the run time is still reasonable ~9 secs.
                // Just using `if (_len > 0) return (_len * 820243) ^ (*_pointer);` gives 5.8 secs.
                // By just returning 0 - the worst possible hash function and linear search - the run time is 12x slower at 56 seconds. 

                // The magic number 820243 is the largest happy prime that contains 2024 from https://prime-numbers.info/list/happy-primes-page-9

                return length switch
                {
                    > 3 => (length * 820243) ^ MemoryMarshal.AsRef<int>(GetSpan()),
                    //> 3 => HashCode.Combine(length , MemoryMarshal.AsRef<int>(GetSpan())),
                    > 1 => MemoryMarshal.AsRef<short>(GetSpan()),
                    > 0 => GetSpan()[0],
                    _ => 0
                };
                //return GetSpan().GetHashCode();
            }

            public override string ToString() => Encoding.UTF8.GetString(GetSpan());
        }

    }
}