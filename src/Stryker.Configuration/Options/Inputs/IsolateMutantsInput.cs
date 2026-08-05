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

    public bool Validate() => SuppliedInput ?? Default!.Value;
}
