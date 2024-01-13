using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using Microsoft.Win32.SafeHandles;

namespace _1brc.hrouidi
{
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