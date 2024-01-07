using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace _1brc.hrouidi
{
    public class DoubleParser
    {
        //private static readonly double[] _powersOf10 = Init10Powers();

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

        private static double[] Init10Powers()
        {
            double[] ret = new double[64];
            for (int i = 0; i < 64; i++)
                ret[i] = 1 / Math.Pow(10, i);

            return ret;
        }
    }
}