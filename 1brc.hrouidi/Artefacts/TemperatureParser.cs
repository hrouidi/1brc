using System.Runtime.CompilerServices;

namespace OneBrc.HRouidi
{
    public static class TemperatureParser
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ParseSimple(ReadOnlySpan<byte> span, out int consumedBytes)
        {
            int pos = 0;//+ nextStart;
            pos += span[pos+1] == '-' ? 1 : 0; 
            float sign = span[pos] == '-' ? -1 : 1;
            float case1 = span[pos + 1] - 48 + 0.1f * (span[pos + 3] - 48); // 9.1
            float case2 = 10 * (span[pos + 1] - 48) + (span[pos + 2] - 48) + 0.1f * (span[pos + 4] - 48); // 92.1
            float value = span[pos + 2] == '.' ? case1 : case2;
            value *= sign;
            consumedBytes = 5 + pos +(span[pos + 2] == '.' ? 0 : 1);
            return value;
        }
    }
}