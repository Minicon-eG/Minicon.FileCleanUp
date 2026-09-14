namespace Minicon.FileCleanUp;

/// <summary>External randomness boundary for repeatable retry tests. Return a factor between 0.8 and 1.2.</summary>
public interface IRetryJitter
{
    double NextFactor();
}
