using Microsoft.Extensions.Logging;
using Stryker.Abstractions.Exceptions;
using Stryker.Utilities.Logging;

namespace Stryker.Configuration.Options.Inputs;

public class CoverageCohortSizeInput : Input<int?>
{
    private const int Maximum = 1024;

    public override int? Default => 1;

    protected override string Description => @"How many tests the Microsoft Test Platform runner captures coverage for per request.
1 is per-test attribution and matches upstream behaviour; it is the default and should normally be left alone.
Values above 1 batch several tests behind a single coverage flush, which is faster to capture but records
every test in the batch as covering everything the batch touched. A mutant is then assessed against tests
that do not cover it, and on a suite with any order or state sensitivity those tests can fail for unrelated
reasons and the mutant is reported killed. Raising this trades reported accuracy for capture speed.";

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

        if (value > 1)
        {
            logger ??= ApplicationLogging.LoggerFactory.CreateLogger<CoverageCohortSizeInput>();
            logger.LogWarning(
                "Coverage cohort size is {CoverageCohortSize}. Coverage is shared across each cohort, so mutants "
                + "are assessed against tests that do not cover them and may be reported killed by unrelated "
                + "failures. This deviates from per-test coverage as implemented upstream; results are not "
                + "comparable to a cohort size of 1.", value);
        }

        return value;
    }
}
