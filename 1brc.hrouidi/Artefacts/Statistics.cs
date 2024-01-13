using System.Runtime.CompilerServices;

namespace _1brc.hrouidi
{
    public struct Statistics
    {
        public double Min;
        public double Max;
        public double Sum;
        public long Count;

        public Statistics()
        {
            Min = float.MaxValue;
            Max = float.MinValue;
            Sum = 0;
            Count = 0;
        }

        public double Average => Sum / Count;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Apply(double value)
        {
            Min = Math.Min(Min, value);
            Max = Math.Max(Max, value);
            Sum += value;
            Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Apply(Statistics other)
        {
            Min = other.Min < Min ? other.Min : Min;
            Max = other.Max > Max ? other.Max : Max;
            Sum += other.Sum;
            Count += other.Count;
        }

        public override string ToString() => $"{Min:N2}/{Average:N2}/{Max:N2}";
    }
}