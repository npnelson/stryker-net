using Microsoft.Extensions.Logging;
using Stryker.Abstractions.Exceptions;
using Stryker.Utilities.Logging;

namespace Stryker.Configuration.Options.Inputs;

public class CoverageCohortSizeInput : Input<int?>
{
    private const int Maximum = 1024;

    public override int? Default => 32;

    protected override string Description => @"How many tests the Microsoft Test Platform runner captures coverage for per request.
Coverage is conservatively shared by every test in a cohort, so a mutant's assessing-test set can be up to
this many times wider than the truth. Larger values make the coverage phase faster and the mutation phase
slower and hungrier for memory; smaller values do the reverse, at the cost of one flush handshake per
request. 1 gives exact per-test attribution.";

    public int Validate(ILogger<CoverageCohortSizeInput> logger = null)
    {
        var value = SuppliedInput ?? Default!.Value;

        if (value < 1)
        {
            throw new InputException("Coverage cohort size must be at least 1.");
        }

        if (value > Maximum)
        {
            throw new InputException($"Coverage cohort size must be at most {Maximum}.");
        }

        if (value != Default)
        {
            logger ??= ApplicationLogging.LoggerFactory.CreateLogger<CoverageCohortSizeInput>();
            logger.LogInformation("Using a coverage cohort size of {CoverageCohortSize} (default {Default}).", value, Default);
        }

        return value;
    }
}
