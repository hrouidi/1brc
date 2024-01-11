using System.Buffers;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;


namespace _1brc.hrouidi
{
    public class DoubleParser
    {

        private static readonly SearchValues<byte> _values = SearchValues.Create("-.0123456789"u8);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ParseNaive(ReadOnlySpan<byte> span, out int consumedBytes)
        {
            int sign = 1;
            bool hasDot = false;

            uint whole = 0;
            double fraction = 0;


            int i = 0;
            ref byte start = ref MemoryMarshal.GetReference(span);

            for (; i < span.Length; i++)
            {
                byte current = Unsafe.Add(ref start, i);

                if (_values.Contains(current) == false)
                    break;


                if (current == (byte)'-')
                {
                    sign = -1;
                }
                else if (current == (byte)'.')
                {
                    hasDot = true;
                }
                else
                {
                    uint digit = ((uint)current - 48);// '0';

                    if (hasDot)
                    {
                        //fractionCount++;
                        fraction = fraction / 10d + digit;
                    }
                    else
                    {
                        whole = whole * 10 + digit;
                    }
                }
            }

            consumedBytes = i;

            return sign * (whole + fraction);//* _powersOf10[fractionCount]);
        }
    }

    public class Helpers
    {
        private static readonly byte[] _newLineBytes = Environment.NewLine.Select(x => (byte)x).ToArray();
        private static readonly byte _firstByte = _newLineBytes[0];

        public static readonly int NewLineBytesCount = _newLineBytes.Length;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOfNewline(ReadOnlySpan<byte> span) => span.IndexOf(_firstByte);



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




    }

    public static class MemoryMappedViewAccessorExtensions
    {
        public static unsafe byte* AsPointer(this MemoryMappedViewAccessor accessor, long offset = 0)
        {
            nint handle = accessor.SafeMemoryMappedViewHandle.DangerousGetHandle();
            return (byte*)handle.ToPointer() + offset;
        }

        public static unsafe ReadOnlySpan<byte> AsSpan(this MemoryMappedViewAccessor accessor, long offset, int length)
        {
            nint handle = accessor.SafeMemoryMappedViewHandle.DangerousGetHandle();
            return new ReadOnlySpan<byte>((byte*)handle.ToPointer() + offset, length);
        }

        public static unsafe ReadOnlySpan<byte> AsSpan0(this MemoryMappedViewAccessor accessor, long offset, int length)
        {
            byte* ptr = (byte*)0;
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            return new ReadOnlySpan<byte>(ptr + offset, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<byte> UnsafeSlice(this in ReadOnlySpan<byte> span, int start)
        {
            return new ReadOnlySpan<byte>(ref Unsafe.Add(ref MemoryMarshal.GetReference(span), start));
        }

    }

    public sealed unsafe class Mmf : IDisposable
    {
        private readonly SafeFileHandle _file;
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _va;

        public long FileLength;
        public byte* DataPtr;

        public Mmf(string filePath)
        {
            _file = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
            FileLength = RandomAccess.GetLength(_file);
            _mmf = MemoryMappedFile.CreateFromFile(_file, $"{Path.GetFileName(filePath)}", FileLength, MemoryMappedFileAccess.Read, HandleInheritability.None, true);
            _va = _mmf.CreateViewAccessor(0, FileLength, MemoryMappedFileAccess.Read);
            DataPtr = _va.AsPointer();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> AsSpan(long offset, int length) => new(DataPtr + offset, length);

        public void Dispose()
        {
            _file.Dispose();
            _mmf.Dispose();
            _va.Dispose();
        }
    }
}