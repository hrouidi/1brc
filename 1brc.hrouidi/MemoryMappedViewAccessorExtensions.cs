using System;
using System.IO.MemoryMappedFiles;

namespace _1brc.hrouidi
{
    public static class MemoryMappedViewAccessorExtensions
    {
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

    }
}