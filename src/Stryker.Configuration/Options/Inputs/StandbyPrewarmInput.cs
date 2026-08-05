namespace Stryker.Configuration.Options.Inputs;

public class StandbyPrewarmInput : Input<bool?>
{
    public override bool? Default => false;

    protected override string Description => @"Overlap test-host startup with the previous mutant's run when --isolate-mutants is set.
Hides most of the per-mutant host start, but a [ModuleInitializer] runs at host start - before the standby
host is told which mutant it will serve - so mutants in or reachable from one never activate and are
reported as survived. Leave this off unless the code under test, and the test project that loads it, are
free of module initializers that touch mutated code.";

    public bool Validate() => SuppliedInput ?? Default!.Value;
}
