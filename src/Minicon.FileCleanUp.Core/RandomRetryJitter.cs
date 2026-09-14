namespace Minicon.FileCleanUp;

public sealed class RandomRetryJitter : IRetryJitter
{
    public double NextFactor() => 0.8 + Random.Shared.NextDouble() * 0.4;
}
