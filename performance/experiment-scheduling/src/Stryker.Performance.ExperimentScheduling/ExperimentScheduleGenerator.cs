namespace Stryker.Performance.ExperimentScheduling;

/// <summary>
/// Creates deterministic schedules from balanced ABBA and BAAB blocks.
/// </summary>
public static class ExperimentScheduleGenerator
{
    /// <summary>
    /// Identifies the block construction, shuffle, and pseudo-random number generator contract.
    /// </summary>
    public const string Version = "stryker-abba-baab-xorshift64star/v1";

    private const int ObservationsPerBlock = 4;
    private const int MaximumBlockCount = 10_000;

    private static readonly string[] _abba = ["A", "B", "B", "A"];
    private static readonly string[] _baab = ["B", "A", "A", "B"];

    /// <summary>
    /// Materializes a balanced schedule before any subject execution begins.
    /// </summary>
    /// <param name="seed">A signed seed whose full bit pattern determines block order.</param>
    /// <param name="blockCount">An even number of blocks between 2 and 10,000.</param>
    /// <returns>A complete schedule containing equal numbers of ABBA and BAAB blocks.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="blockCount"/> is odd or outside the supported range.
    /// </exception>
    public static ExperimentSchedule Generate(long seed, int blockCount)
    {
        ValidateBlockCount(blockCount);

        var patterns = Enumerable.Range(0, blockCount)
            .Select(index => index < blockCount / 2 ? _abba : _baab)
            .ToArray();

        var random = new StableRandom(unchecked((ulong)seed));
        for (var index = patterns.Length - 1; index > 0; index--)
        {
            var swapIndex = random.Next(index + 1);
            (patterns[index], patterns[swapIndex]) = (patterns[swapIndex], patterns[index]);
        }

        var observations = new ScheduledObservation[blockCount * ObservationsPerBlock];
        var sequenceIndex = 0;

        for (var blockIndex = 0; blockIndex < patterns.Length; blockIndex++)
        {
            for (var positionIndex = 0; positionIndex < ObservationsPerBlock; positionIndex++)
            {
                var block = blockIndex + 1;
                var position = positionIndex + 1;
                observations[sequenceIndex] = new ScheduledObservation(
                    ObservationId(block, position),
                    sequenceIndex + 1,
                    block,
                    position,
                    patterns[blockIndex][positionIndex]);
                sequenceIndex++;
            }
        }

        return new ExperimentSchedule(Version, seed, blockCount, observations);
    }

    internal static bool IsSupportedBlockCount(int blockCount) =>
        blockCount is >= 2 and <= MaximumBlockCount && blockCount % 2 == 0;

    internal static string ObservationId(int block, int position) => $"b{block:D4}.p{position}";

    private static void ValidateBlockCount(int blockCount)
    {
        if (!IsSupportedBlockCount(blockCount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(blockCount),
                blockCount,
                "Block count must be an even number between 2 and 10,000.");
        }
    }

    private sealed class StableRandom(ulong seed)
    {
        private ulong _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

        internal int Next(int exclusiveUpperBound)
        {
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            var value = _state * 2685821657736338717UL;

            return (int)(value % (uint)exclusiveUpperBound);
        }
    }
}
