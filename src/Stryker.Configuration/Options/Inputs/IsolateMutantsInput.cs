using Microsoft.Extensions.Logging;
using Stryker.Abstractions.Options;
using Stryker.Utilities.Logging;

namespace Stryker.Configuration.Options.Inputs;

public class IsolateMutantsInput : Input<bool?>
{
    public override bool? Default => false;

    protected override string Description => @"Run every mutant in its own test-host process.
By default the Microsoft Test Platform runner reuses one host across mutant sessions and switches the
active mutant between runs. Anything the code under test caches in process-global state is then computed
once, under whichever mutant was active at the time, and observed by every later mutant on that host -
which can report provably surviving mutants as killed. Enable this when the code under test memoises in
static state. Costs one test-host start per mutant.";

    public bool Validate(TestRunner testRunner = TestRunner.VsTest, ILogger<IsolateMutantsInput> logger = null)
    {
        var value = SuppliedInput ?? Default!.Value;

        // Only the Microsoft Test Platform runner reuses a host across mutant sessions, so only it can
        // honour this. Say so rather than accepting the flag and ignoring it.
        if (value && testRunner != TestRunner.MicrosoftTestPlatform)
        {
            logger ??= ApplicationLogging.LoggerFactory.CreateLogger<IsolateMutantsInput>();
            logger.LogWarning(
                "Mutant process isolation was requested but only applies to the Microsoft Test Platform runner; "
                + "it is ignored for the {TestRunner} runner.", testRunner);
        }

        return value;
    }
}
