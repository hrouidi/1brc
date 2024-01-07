﻿using System.Buffers.Text;
using System.Diagnostics;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace _1brc
{
    public unsafe class App : IDisposable
    {
        private readonly FileStream _fileStream;
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _va;
        private readonly SafeMemoryMappedViewHandle _vaHandle;
        private readonly byte* _pointer;
        private readonly long _fileLength;

        private readonly int _initialChunkCount;

        private const int MaxChunkSize = int.MaxValue - 100_000;

        public string FilePath { get; }

        private static readonly double[] _powersOf10 = new double[64];
        private static GCHandle _powersHandle;
        private static readonly double* _powersPtr = Init10Powers();

        private static double* Init10Powers()
        {
            for (int i = 0; i < 64; i++)
            {
                _powersOf10[i] = 1 / Math.Pow(10, i);
            }

            _powersHandle = GCHandle.Alloc(_powersOf10, GCHandleType.Pinned);
            return (double*)_powersHandle.AddrOfPinnedObject();
        }

        public App(string filePath, int? chunkCount = null)
        {
            _initialChunkCount = Math.Max(1, chunkCount ?? Environment.ProcessorCount);
            FilePath = filePath;

            _fileStream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
            var fileLength = _fileStream.Length;
            _mmf = MemoryMappedFile.CreateFromFile(_fileStream, $@"{Path.GetFileName(FilePath)}", fileLength, MemoryMappedFileAccess.Read, HandleInheritability.None, true);

            byte* ptr = (byte*)0;
            _va = _mmf.CreateViewAccessor(0, fileLength, MemoryMappedFileAccess.Read);
            _vaHandle = _va.SafeMemoryMappedViewHandle;
            _vaHandle.AcquirePointer(ref ptr);

            _pointer = ptr;
            _fileLength = fileLength;
        }

        public List<(long start, int length)> SplitIntoMemoryChunks()
        {
            // We want equal chunks not larger than int.MaxValue
            // We want the number of chunks to be a multiple of CPU count, so multiply by 2
            // Otherwise with CPU_N+1 chunks the last chunk will be processed alone.

            var chunkCount = _initialChunkCount;
            var chunkSize = _fileLength / chunkCount;
            while (chunkSize > MaxChunkSize)
            {
                chunkCount *= 2;
                chunkSize = _fileLength / chunkCount;
            }

            List<(long start, int length)> chunks = new();

            long pos = 0;

            for (int i = 0; i < chunkCount; i++)
            {
                if (pos + chunkSize >= _fileLength)
                {
                    chunks.Add((pos, (int)(_fileLength - pos)));
                    break;
                }

                var newPos = pos + chunkSize;
                var sp = new ReadOnlySpan<byte>(_pointer + newPos, (int)chunkSize);
                var idx = IndexOfNewlineChar(sp, out var stride);
                newPos += idx + stride;
                var len = newPos - pos;
                chunks.Add((pos, (int)(len)));
                pos = newPos;
            }

            return chunks;
        }

        public List<(long start, int length)> SplitMock() => [(0, (int)_fileLength)];

        public Dictionary<Utf8Span, Summary> ProcessChunk(long start, int length)
        {
            
            var result = new Dictionary<Utf8Span, Summary>();

            var pos = 0;

            while (pos < length)
            {
                byte* ptr = _pointer + start + pos;

                var sp = new ReadOnlySpan<byte>(ptr, length);

                int sepIdx = sp.IndexOf((byte)';');

                ref Summary summary = ref CollectionsMarshal.GetValueRefOrAddDefault(result, new Utf8Span(ptr, sepIdx), out var exists);

                sepIdx++;

                sp = sp.Slice(sepIdx);

                var nlIdx = IndexOfNewlineChar(sp, out var stride);

                var value = ParseNaive(sp[..nlIdx]);

                if (exists)
                    summary.Apply(value);
                else
                    summary.Init(value);

                pos += sepIdx + nlIdx + stride;
            }

            return result;
        }

        public Dictionary<Utf8Span, Summary> Process()
        {
            //List<(long start, int length)> chunkRanges = SplitIntoMemoryChunks();
            List<(long start, int length)> chunkRanges = SplitMock();
            List<Dictionary<Utf8Span, Summary>> chunks = chunkRanges
                                                         //.AsParallel()
                                                         .Select((tuple => ProcessChunk(tuple.start, tuple.length)))
                                                         .ToList();

            Dictionary<Utf8Span, Summary>? result = null;

            foreach (Dictionary<Utf8Span, Summary> chunk in chunks)
            {
                if (result == null)
                {
                    result = chunk;
                    continue;
                }

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
            
            foreach ((Utf8Span key, Summary value)  in result.OrderBy(x => x.Key.ToString())) 
                Console.WriteLine($"{key} = {value}");

            sw.Stop();
            Console.WriteLine($"Processed in {sw.Elapsed}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int IndexOfNewlineChar(ReadOnlySpan<byte> span, out int stride)
        {
            stride = default;
            int idx = span.IndexOfAny((byte)'\n', (byte)'\r');
            if ((uint)idx < (uint)span.Length)
            {
                stride = 1;
                if (span[idx] == '\r')
                {
                    int nextCharIdx = idx + 1;
                    if ((uint)nextCharIdx < (uint)span.Length && span[nextCharIdx] == '\n')
                    {
                        stride = 2;
                    }
                }
            }

            return idx;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double ParseNaive(ReadOnlySpan<byte> span)
        {
            double sign = 1;
            bool hasDot = false;

            ulong whole = 0;
            ulong fraction = 0;
            int fractionCount = 0;

            for (int i = 0; i < span.Length; i++)
            {
                var c = (int)span[i];

                if (c == (byte)'-' && !hasDot && sign == 1 && whole == 0)
                {
                    sign = -1;
                }
                else if (c == (byte)'.' && !hasDot)
                {
                    hasDot = true;
                }
                else if ((uint)(c - '0') <= 9)
                {
                    var digit = c - '0';

                    if (hasDot)
                    {
                        fractionCount++;
                        fraction = fraction * 10 + (ulong)digit;
                    }
                    else
                    {
                        whole = whole * 10 + (ulong)digit;
                    }
                }
                else
                {
                    // Fallback to the full impl on any irregularity
                    return double.Parse(span, NumberStyles.Float);
                }
            }

            return sign * (whole + fraction * _powersPtr[fractionCount]);
        }

        public void Dispose()
        {
            _vaHandle.Dispose();
            _va.Dispose();
            _mmf.Dispose();
            _fileStream.Dispose();
        }
    }
}