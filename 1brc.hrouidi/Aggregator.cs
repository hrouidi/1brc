using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace _1brc.hrouidi
{
    public class Aggregator(string filePath) : IDisposable
    {
        private readonly Mmf _mmf = new(filePath);
        private readonly int _initialChunkCount = Environment.ProcessorCount;


        #region Generate File chuncks

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

        #endregion

        public Dictionary<Utf8Span, Statistics> Process()
        {
            List<(long start, int length)> chunkRanges = SplitIntoMemoryChunks();

            Dictionary<Utf8Span, Statistics>[] chunks = Enumerable.Range(0, Environment.ProcessorCount)
                                                                  .Select(static _ => new Dictionary<Utf8Span, Statistics>(512))
                                                                  .ToArray();

            Parallel.ForEach(chunkRanges, (x, _, tid) => ProcessChunk1(chunks[(int)tid], x.start, x.length));


            Dictionary<Utf8Span, Statistics> result = chunks[0];

            foreach (Dictionary<Utf8Span, Statistics> chunk in chunks[1..])
            {
                foreach (KeyValuePair<Utf8Span, Statistics> pair in chunk)
                {
                    ref Statistics statistics = ref CollectionsMarshal.GetValueRefOrAddDefault(result, pair.Key, out bool exists);
                    if (exists)
                        statistics.Apply(pair.Value);
                    else
                        statistics = pair.Value;
                }
            }

            return result;
        }
        

        public unsafe Dictionary<Utf8Span, Statistics> ProcessChunk(long start, int length)
        {
            Dictionary<Utf8Span, Statistics> result = new(512);

            ReadOnlySpan<byte> span = _mmf.AsSpan(start, length);
            int spanCurrentPosition = 0;
            while (spanCurrentPosition < length)
            {
                ReadOnlySpan<byte> sp = span.Slice(spanCurrentPosition);
                int sepIdx = sp.IndexOf((byte)';');
                double value = DoubleParser.ParseNaive(sp.Slice(++sepIdx), out int bytes);

                ref Statistics statistics = ref CollectionsMarshal.GetValueRefOrAddDefault(result, new Utf8Span(_mmf.DataPtr + start + spanCurrentPosition, sepIdx), out bool _);
                statistics.Apply(value);

                spanCurrentPosition += sepIdx + bytes + Helpers.NewLineBytesCount;
            }

            return result;
        }

        public unsafe void ProcessChunk11(Dictionary<Utf8Span, Statistics> result, long start, long length)
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
                    ref Statistics statistics = ref CollectionsMarshal.GetValueRefOrAddDefault(result, stationName, out bool _);
                    statistics.Apply(value);
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
                ref Statistics statistics = ref CollectionsMarshal.GetValueRefOrAddDefault(result, new Utf8Span(currentPosition, lastIndex), out bool _);
                statistics.Apply(lastIndex);
            }
        }

        public unsafe void ProcessChunk1(Dictionary<Utf8Span, Statistics> result, long start, long length)
        {
            Vector256<byte> comaVec = Vector256.Create((byte)';');

            byte* startPosition = _mmf.DataPtr + start;
            byte* endPosition = startPosition + length - Vector256<byte>.Count;
            byte* currentPosition = startPosition;
            float value = 0;
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
                value = currentPosition[pos + 2] == '.' ? case1 : case2;
                value *= sign;
                int consumed = 6 + (currentPosition[pos + 3] == '.' ? 1 : 0);

                // /////////////////////// 
                //Utf8Span stationName = new(currentPosition, index);
                //ref Statistics statistics = ref CollectionsMarshal.GetValueRefOrAddDefault(result, stationName, out bool _);
                //statistics.Apply(1);
                Statistics statistics = new();
                statistics.Apply(value);

                currentPosition += index + consumed;
            }
            //ref Statistics summary = ref CollectionsMarshal.GetValueRefOrAddDefault(result, new Utf8Span(currentPosition,2), out bool _);
            //summary.Apply(value);
        }

        public unsafe void ProcessChunk11(Statistics[] result, long start, long length)
        {
            Vector256<byte> comaVec = Vector256.Create((byte)';');

            byte* startPosition = _mmf.DataPtr + start;
            byte* endPosition = startPosition + length - Vector256<byte>.Count;
            byte* currentPosition = startPosition;
            float value = 0;
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
                value = currentPosition[pos + 2] == '.' ? case1 : case2;
                value *= sign;
                int consumed = 6 + (currentPosition[pos + 3] == '.' ? 1 : 0);

                // /////////////////////// 
                Utf8Span stationName = new(currentPosition, index);

                //ref Statistics statistics = ref CollectionsMarshal.GetValueRefOrAddDefault(result, stationName, out bool _);
                //statistics.Apply(1);
                Statistics statistics = new();
                statistics.Apply(value);

                currentPosition += index + consumed;
            }
            //ref Statistics summary = ref CollectionsMarshal.GetValueRefOrAddDefault(result, new Utf8Span(currentPosition,2), out bool _);
            //summary.Apply(value);
        }

        public unsafe void ProcessChunk10(Dictionary<Utf8Span, Statistics> result, long start, long length)
        {
            byte* startPosition = _mmf.DataPtr + start;
            byte* endPosition = startPosition + length;
            //byte* currentPosition = startPosition;
            int entryLength = 0;
            for (byte* currentPosition = startPosition; currentPosition < endPosition; currentPosition++)
            {
                if (*currentPosition == (byte)';')
                {

                    ref Statistics statistics = ref CollectionsMarshal.GetValueRefOrAddDefault(result, new Utf8Span(currentPosition, entryLength), out bool _);
                    statistics.Apply(entryLength);
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
            Dictionary<Utf8Span, Statistics> result = Process();
#if DEBUG
            foreach ((string key1, string key2) in Collisions)
                Console.WriteLine($"### Collision at {key1}:{key2}");
#endif

            foreach ((Utf8Span key, Statistics value) in result.OrderBy(x => x.Key.ToString()))
                //foreach ((Utf8Span key, Statistics value) in result)
                Console.WriteLine($"{key} = {value}");

            Console.WriteLine($"### Count{result.Count}");

            //#if DEBUG
            //            Console.WriteLine($"### Count{result.Count}");
            //#endif
        }

        public void Dispose() => _mmf.Dispose();
    }
}