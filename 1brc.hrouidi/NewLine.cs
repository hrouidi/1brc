using System.Runtime.CompilerServices;


namespace _1brc.hrouidi
{
    public class NewLine
    {
        private static readonly byte[] _newLineBytes = Environment.NewLine.Select(x => (byte)x).ToArray();
        private static readonly byte _firstByte = _newLineBytes[0];

        public static readonly int NewLineBytesCount = _newLineBytes.Length;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOf(ReadOnlySpan<byte> span) => span.IndexOf(_firstByte);

        
        
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
}