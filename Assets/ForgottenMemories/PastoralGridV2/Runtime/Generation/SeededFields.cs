namespace ForgottenMemories.PastoralGridV2
{
    public static class SeededFields
    {
        // 保持盐值与运算顺序稳定，让各阶段的随机场可独立复现。
        public static uint Hash(uint x)
        {
            unchecked
            {
                x ^= x >> 16;
                x *= 0x7feb352dU;
                x ^= x >> 15;
                x *= 0x846ca68bU;
                return x ^ (x >> 16);
            }
        }

        public static FastNoiseLite Perlin(int seed, uint salt)
        {
            var field = new FastNoiseLite(unchecked(seed ^ (int)salt));
            field.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
            field.SetFrequency(1);
            return field;
        }
    }
}
