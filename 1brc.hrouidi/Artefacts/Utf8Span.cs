using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace OneBrc.HRouidi
{
    public readonly unsafe struct Utf8Span : IEquatable<Utf8Span>,IComparable<Utf8Span>
    {
        internal readonly byte* Pointer;
        internal readonly int Length;

        public Utf8Span(byte* pointer, int length)
        {
            Debug.Assert(length >= 0);
            Pointer = pointer;
            Length = length;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> GetSpan() => new(Pointer, Length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Utf8Span other)
        {
            var ret = GetSpan().SequenceEqual(other.GetSpan());
            return ret;
        }

        public override bool Equals(object? obj) => obj is Utf8Span other && Equals(other);

        public override int GetHashCode()
        {
            var ret = Length switch
            {
                >= 8 => Length * 820243 ^ *(int*)Pointer + *(int*)(Pointer + 4),
                >= 4 => Length * 820243 ^ *(int*)Pointer,
                _ => *(ushort*)Pointer // length == 3 ( 2 cases)
            };

            return ret % 521;
        }

        public override string ToString() => new((sbyte*)Pointer, 0, Length, Encoding.UTF8);

        public int CompareTo(Utf8Span other) => GetSpan().SequenceCompareTo(other.GetSpan());
    }
}
