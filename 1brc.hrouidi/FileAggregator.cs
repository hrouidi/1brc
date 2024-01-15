using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace OneBrc.HRouidi
{
    public class FileAggregator(string filePath) : IDisposable
    {
        internal readonly Mmf Mmf = new(filePath);

        #region Generate File chuncks

        internal static List<(long start, int length)> GenerateProcessorChunks(Mmf mmf)
        {
            if (mmf.FileLength <= 4096)
                return [(0, (int)mmf.FileLength)];

            int chunkCount = Environment.ProcessorCount;
            long chunkSize = mmf.FileLength / chunkCount;

            List<(long start, int length)> chunks = new(chunkCount);

            long pos = 0;

            for (int i = 0; i < chunkCount; i++)
            {
                if (pos + chunkSize >= mmf.FileLength)
                {
                    chunks.Add((pos, (int)(mmf.FileLength - pos)));
                    break;
                }

                long newPos = pos + chunkSize;
                ReadOnlySpan<byte> sp = mmf.AsSpan(newPos, (int)chunkSize);
                int idx = sp.IndexOf((byte)'\n');
                newPos += idx + 1;
                long len = newPos - pos;
                chunks.Add((pos, (int)len));
                pos = newPos;
            }

            return chunks;
        }

        internal static List<(long start, long length)> GenerateAlignedChunks(long fileLength, int alignment, out int subBlockSize)
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

        internal Dictionary<Utf8Span, Statistics> Process()
        {
            List<(long start, int length)> chunkRanges = GenerateProcessorChunks(Mmf);

            Dictionary<Utf8Span, Statistics>[] chunks = chunkRanges.Select(static _ => new Dictionary<Utf8Span, Statistics>(512))
                                                                   .ToArray();

            Parallel.ForEach(chunkRanges, (x, _, tid) => ProcessChunk(chunks[(int)tid], x.start, x.length));


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

        internal unsafe void ProcessChunk(Dictionary<Utf8Span, Statistics> result, long start, int length)
        {
            ProcessChunk(result, Mmf.DataPtr, start, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void ProcessChunk0(Dictionary<Utf8Span, Statistics> result, byte* data, long start, int length)
        {
            byte* startPtr = data + start;
            ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(startPtr, length);

            int spanCurrentPosition = 0;
            while (spanCurrentPosition < length)
            {
                ReadOnlySpan<byte> sp = span[spanCurrentPosition..];
                int sepIdx = sp.IndexOf((byte)';');
                float value = TemperatureParser.Parse(sp[sepIdx..], out int bytes);

                Utf8Span stationName = new(startPtr + spanCurrentPosition, sepIdx);
                ref Statistics statistics = ref CollectionsMarshal.GetValueRefOrAddDefault(result, stationName, out bool _);
                statistics.Apply(value);

                spanCurrentPosition += sepIdx + bytes;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void ProcessChunk(Dictionary<Utf8Span, Statistics> result, byte* data, long start, int length)
        {
            byte* startPtr = data + start;
            byte* endPtr = startPtr + length;
            //ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(startPtr, length);

            byte* spanCurrentPosition = startPtr;
            while (spanCurrentPosition < endPtr)
            {
                //ReadOnlySpan<byte> sp = new ReadOnlySpan<byte>(spanCurrentPosition,length-(int)(spanCurrentPosition- startPtr));
                ReadOnlySpan<byte> sp = new ReadOnlySpan<byte>(spanCurrentPosition, (int)(endPtr - spanCurrentPosition));
                int sepIdx = sp.IndexOf((byte)';');
                float value = TemperatureParser.UnsafeParse(spanCurrentPosition + sepIdx, out int bytes);

                Utf8Span stationName = new(spanCurrentPosition, sepIdx);
                ref Statistics statistics = ref CollectionsMarshal.GetValueRefOrAddDefault(result, stationName, out bool _);
                statistics.Apply(value);

                spanCurrentPosition += sepIdx + bytes;
            }
        }

        internal unsafe void ProcessChunk1(Dictionary<Utf8Span, Statistics> result, long start, long length)
        {
            Vector256<byte> comaVec = Vector256.Create((byte)';');

            byte* startPosition = Mmf.DataPtr + start;
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
                int consumed = 6 + (currentPosition[pos + 2] == '.' ? 0 : 1);

                // /////////////////////// 
                Utf8Span stationName = new(currentPosition, index);
                ref Statistics statistics = ref CollectionsMarshal.GetValueRefOrAddDefault(result, stationName, out bool _);
                //statistics.Apply(1);
                //Statistics statistics = new();
                statistics.Apply(value);

                currentPosition += index + consumed;
            }
            //ref Statistics summary = ref CollectionsMarshal.GetValueRefOrAddDefault(result, new Utf8Span(currentPosition,2), out bool _);
            //summary.Apply(value);
        }

        internal unsafe void ProcessChunk11(Dictionary<Utf8Span, Statistics> result, long start, long length)
        {
            Vector256<byte> comaVec = Vector256.Create((byte)';');

            byte* startPosition = Mmf.DataPtr + start;
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

        internal unsafe void ProcessChunk11(Statistics[] result, long start, long length)
        {
            Vector256<byte> comaVec = Vector256.Create((byte)';');

            byte* startPosition = Mmf.DataPtr + start;
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

        internal unsafe void ProcessChunk10(Dictionary<Utf8Span, Statistics> result, long start, long length)
        {
            byte* startPosition = Mmf.DataPtr + start;
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


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static string SortAndDumpToString(Dictionary<Utf8Span, Statistics> result)
        {
            var tmp = string.Join(", ", result.OrderBy(x => x.Key)
                                              .Select(x => $"{x.Key}={x.Value}"));
            return $"{{{tmp}}}";
        }

        public (TimeSpan processTimeSpan, TimeSpan sortAndPrintTimeSpan) ProcessAndPrintResult()
        {
            Stopwatch sw = Stopwatch.StartNew();
            Dictionary<Utf8Span, Statistics> result = Process();
            sw.Stop();
            TimeSpan processTimeSpan = sw.Elapsed;
            sw.Restart();
            Console.WriteLine(SortAndDumpToString(result));
            sw.Stop();
            //Console.WriteLine($"### Count{result.Count}");
            return (processTimeSpan, sw.Elapsed);
        }

        public void Dispose() => Mmf.Dispose();
    }
}