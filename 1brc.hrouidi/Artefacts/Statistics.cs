using System.Globalization;
using System.Runtime.CompilerServices;

namespace OneBrc.HRouidi
{
    public struct Statistics()
    {
        float Min = 1024;
        float Max = -1024;
        float Sum = 0;
        int Count = 0;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Apply(float value)
        {
            Min = Count != 0 ? MathF.Min(Min, value) : value;
            Max = Count != 0 ? MathF.Max(Max, value) : value;
            Sum += value;
            Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Apply(Statistics other)
        {
            Min = MathF.Min(other.Min, Min);
            Max = MathF.Max(other.Max, Max);
            Sum += other.Sum;
            Count += other.Count;
        }

        private float Average => Sum / Count;

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0:N1}/{1:N1}/{2:N1}", Min, MathF.Round(Average,1, MidpointRounding.AwayFromZero), Max);
    }
}