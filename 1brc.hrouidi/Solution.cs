using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using _1brc.hrouidi;

namespace _1brc
{
    public class Solution(string filePath) : IDisposable
    {
        private readonly Mmf _mmf = new(filePath);
        private readonly int _initialChunkCount = Environment.ProcessorCount;

        public List<(long start, int length)> SplitIntoMemoryChunks()
        {

            // We want equal chunks not larger than int.MaxValue
            // We want the number of chunks to be a multiple of CPU count, so multiply by 2
            // Otherwise with CPU_N+1 chunks the last chunk will be processed alone.

            const int maxChunkSize = int.MaxValue - 100_000;

            int chunkCount = _initialChunkCount;
            long chunkSize = _mmf.FileLength / chunkCount;
            while (chunkSize > maxChunkSize)
            {
                chunkCount *= 2;
                chunkSize = _mmf.FileLength / chunkCount;
            }

            List<(long start, int length)> chunks = new(chunkCount);

            long pos = 0;

            for (int i = 0; i < chunkCount; i++)
            {
                if (pos + chunkSize >= _mmf.FileLength)
                {
                    chunks.Add((pos, (int)(_mmf.FileLength - pos)));
                    break;
                }

                long newPos = pos + chunkSize;
                //ReadOnlySpan<byte> sp = new ReadOnlySpan<byte>(_pointer + newPos, (int)chunkSize);
                ReadOnlySpan<byte> sp = _mmf.AsSpan(newPos, (int)chunkSize);
                //var idx = IndexOfNewlineChar(sp, out var stride);
                int idx = Helpers.IndexOfNewline(sp);
                newPos += idx + Helpers.NewLineBytesCount;
                long len = newPos - pos;
                chunks.Add((pos, (int)len));
                pos = newPos;
            }

            return chunks;
        }

        public unsafe Dictionary<Utf8Span, Summary> ProcessChunk(long start, int length)
        {
            Dictionary<Utf8Span, Summary> result = new(512);

            ReadOnlySpan<byte> span = _mmf.AsSpan(start, length);
            int spanCurrentPosition = 0;
            while (spanCurrentPosition < length)
            {
                ReadOnlySpan<byte> sp = span.Slice(spanCurrentPosition);
                //Avx2.LoadAlignedVector256NonTemporal()
                int sepIdx = sp.IndexOf((byte)';');
                double value = DoubleParser.ParseNaive(sp.Slice(++sepIdx), out int bytes);

                ref Summary summary = ref CollectionsMarshal.GetValueRefOrAddDefault(result, new Utf8Span(_mmf.DataPtr + start + spanCurrentPosition, sepIdx), out bool _);
                summary.Apply(value);

                spanCurrentPosition += sepIdx + bytes + Helpers.NewLineBytesCount;
            }

            return result;
        }

        public Dictionary<Utf8Span, Summary> Process()
        {

            List<(long start, int length)> chunkRanges = SplitIntoMemoryChunks();


            Dictionary<Utf8Span, Summary>[] chunks = chunkRanges.AsParallel()
                                                                .Select(x => ProcessChunk(x.start, x.length))
                                                                .ToArray();


            Dictionary<Utf8Span, Summary> result = chunks[0];

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

            return result;
        }

        public Dictionary<Utf8Span, Summary> Process1()
        {

            //List<(long start, long length)> chunkRanges = Chunkify(_mmf.FileLength, 1, out int rem);
            List<(long start, int length)> chunkRanges = SplitIntoMemoryChunks();


            Dictionary<Utf8Span, Summary>[] chunks = Enumerable.Range(0, Environment.ProcessorCount)
                                                               .Select(static _ => new Dictionary<Utf8Span, Summary>(512))
                                                               .ToArray();

            Parallel.ForEach(chunkRanges, (x, _, tid) => ProcessChunk1(chunks[(int)tid], x.start, x.length));

            Dictionary<Utf8Span, Summary> result = chunks[0];

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

            return result;
        }

        public static List<(long start, long length)> Chunkify(long fileLength, int alignment, out int subBlockSize)
        {
            int chunkCount = Environment.ProcessorCount;
            //(long chunkSize, long rem) = long.DivRem(fileLength, chunkCount);
            (long blocksCount, long blockRem) = long.DivRem(fileLength, alignment);
            (long blocksPerChunk, long blocksPerChunkRem) = long.DivRem(blocksCount, chunkCount);

            long chunkSize = blocksPerChunk * alignment;

            List<(long start, long length)> chunks = new(chunkCount);

            long start = 0;
            for (int tid = 0; tid < chunkCount; tid++)
            {
                long fixedSize = chunkSize + (blocksPerChunkRem-- > 0 ? alignment : 0);
                chunks.Add((start, fixedSize));
                start += fixedSize;
            }

            subBlockSize = (int)blockRem;
            return chunks;
        }

        public unsafe void ProcessChunk11(Dictionary<Utf8Span, Summary> result, long start, long length)
        {
            Vector256<byte> comaVec = Vector256.Create((byte)';');

            byte* startPosition = _mmf.DataPtr + start;
            byte* endPosition = startPosition + length - Vector256<byte>.Count;
            byte* currentPosition = startPosition;

            int nextStart = 0;
            for (; currentPosition < endPosition; currentPosition += Vector256<byte>.Count)
            {
                Vector256<byte> vector = Vector256.Load(currentPosition);
                Vector256<byte> comaEq = Vector256.Equals(vector, comaVec);
                uint mask = (uint)Avx2.MoveMask(comaEq);
                while (mask != 0)
                {
                    int index = BitOperations.TrailingZeroCount(mask);
                    /////////////////////
                    int pos = index;//+ nextStart;

                    pos += currentPosition[pos + 1] == '-' ? 1 : 0; // after this, data[pos] = position right before first digit
                    float sign = currentPosition[pos] == '-' ? -1 : 1;
                    float case1 = currentPosition[pos + 1] - 48 + 0.1f * (currentPosition[pos + 3] - 48); // 9.1
                    float case2 = 10 * (currentPosition[pos + 1] - 48) + (currentPosition[pos + 2] - 48) + 0.1f * (currentPosition[pos + 4] - 48); // 92.1
                    float value = currentPosition[pos + 2] == '.' ? case1 : case2;
                    value *= sign;
                    int consumed = 6 + (currentPosition[pos + 3] == '.' ? 1 : 0);

                    // /////////////////////// 
                    Utf8Span stationName = new(currentPosition + nextStart, index);
                    ref Summary summary = ref CollectionsMarshal.GetValueRefOrAddDefault(result, stationName, out bool _);
                    summary.Apply(value);
                    mask >>>= index + consumed;
                    nextStart = (pos + consumed) % Vector256<byte>.Count;
                } //while (mask != 0);

                nextStart -= Vector256<byte>.Count;
            }
            // reminder

            var span = new ReadOnlySpan<byte>(currentPosition, (int)(startPosition + length - currentPosition));
            var lastIndex = span.IndexOf((byte)';');
            if (lastIndex > 0)
            {
                ref Summary summary = ref CollectionsMarshal.GetValueRefOrAddDefault(result, new Utf8Span(currentPosition, lastIndex), out bool _);
                summary.Apply(lastIndex);
            }
        }

        public unsafe void ProcessChunk1(Dictionary<Utf8Span, Summary> result, long start, long length)
        {
            Vector256<byte> comaVec = Vector256.Create((byte)';');

            byte* startPosition = _mmf.DataPtr + start;
            byte* endPosition = startPosition + length - Vector256<byte>.Count;
            byte* currentPosition = startPosition;

            while (currentPosition < endPosition)
            {
                Vector256<byte> vector = Vector256.Load(currentPosition);
                Vector256<byte> comaEq = Vector256.Equals(vector, comaVec);
                uint mask = (uint)Avx2.MoveMask(comaEq);
                int index = BitOperations.TrailingZeroCount(mask);

                /////////////////////
                int pos = index;//+ nextStart;
                pos += currentPosition[pos + 1] == '-' ? 1 : 0; // after this, data[pos] = position right before first digit
                float sign = currentPosition[pos] == '-' ? -1 : 1;
                float case1 = currentPosition[pos + 1] - 48 + 0.1f * (currentPosition[pos + 3] - 48); // 9.1
                float case2 = 10 * (currentPosition[pos + 1] - 48) + (currentPosition[pos + 2] - 48) + 0.1f * (currentPosition[pos + 4] - 48); // 92.1
                float value = currentPosition[pos + 2] == '.' ? case1 : case2;
                value *= sign;
                int consumed = 6 + (currentPosition[pos + 3] == '.' ? 1 : 0);

                // /////////////////////// 
                Utf8Span stationName = new(currentPosition, index);
                ref Summary summary = ref CollectionsMarshal.GetValueRefOrAddDefault(result, stationName, out bool _);
                summary.Apply(value);

                currentPosition += index + consumed;
            }
        }

        public unsafe void ProcessChunk10(Dictionary<Utf8Span, Summary> result, long start, long length)
        {
            byte* startPosition = _mmf.DataPtr + start;
            byte* endPosition = startPosition + length;
            //byte* currentPosition = startPosition;
            int entryLength = 0;
            for (byte* currentPosition = startPosition; currentPosition < endPosition; currentPosition++)
            {
                if (*currentPosition == (byte)';')
                {

                    ref Summary summary = ref CollectionsMarshal.GetValueRefOrAddDefault(result, new Utf8Span(currentPosition, entryLength), out bool _);
                    summary.Apply(entryLength);
                    entryLength = 0;
                }
                else
                {
                    entryLength++;
                }
            }
        }

#if DEBUG
        public static HashSet<(string, string)> Collisions = new();
#endif

        public void PrintResult()
        {
            Dictionary<Utf8Span, Summary> result = Process1();
#if DEBUG
            foreach ((string key1, string key2) in Collisions)
                Console.WriteLine($"### Collision at {key1}:{key2}");
#endif

            foreach ((Utf8Span key, Summary value) in result.OrderBy(x => x.Key.ToString()))
                //foreach ((Utf8Span key, Summary value) in result)
                Console.WriteLine($"{key} = {value}");

            Console.WriteLine($"### Count{result.Count}");

//#if DEBUG
//            Console.WriteLine($"### Count{result.Count}");
//#endif
        }

        public void Dispose() => _mmf.Dispose();

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
    }
}