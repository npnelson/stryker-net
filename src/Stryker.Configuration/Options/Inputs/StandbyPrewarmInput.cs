using Microsoft.Extensions.Logging;
using Stryker.Utilities.Logging;

namespace Stryker.Configuration.Options.Inputs;

public class StandbyPrewarmInput : Input<bool?>
{
    public override bool? Default => false;

    protected override string Description => @"Overlap test-host startup with the previous mutant's run when --isolate-mutants is set.
Hides most of the per-mutant host start, but a [ModuleInitializer] runs at host start - before the standby
host is told which mutant it will serve - so mutants in or reachable from one never activate and are
reported as survived. Leave this off unless the code under test, and the test project that loads it, are
free of module initializers that touch mutated code.";

    public bool Validate(ILogger<StandbyPrewarmInput> logger = null)
    {
        var value = SuppliedInput ?? Default!.Value;

        if (value)
        {
            logger ??= ApplicationLogging.LoggerFactory.CreateLogger<StandbyPrewarmInput>();
            logger.LogWarning(
                "Standby pre-warming is experimental and produces INCORRECT results when a [ModuleInitializer] "
                + "executes mutated code. A standby host is started before it is told which mutant it will serve, "
                + "so the module initializer runs under a stale mutant id and any mutant it reaches never "
                + "activates - those mutants are reported as Survived rather than Killed. Nothing detects this. "
                + "Only enable it when neither the project under test nor its test projects declare a module "
                + "initializer that reaches mutated code.");
        }

        return value;
    }
}
