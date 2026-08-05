namespace StaticStateProbe;

/// <summary>
/// Process-global state written once during test-host startup and read by every test.
/// </summary>
public static class StartupState
{
    public static bool IsReady { get; private set; }

    internal static void SetReady(bool isReady) => IsReady = isReady;
}

/// <summary>
/// Ordinary instance code that writes the process-global state above. The mutants inside this
/// constructor are NOT flagged by <c>IsStaticValue</c> - it is only the caller (a module initializer
/// in the test assembly) that runs once per process. That is precisely the gap the static-mutant
/// carve-out does not cover.
/// </summary>
public sealed class StartupConfigurator
{
    public StartupConfigurator(int seed)
    {
        StartupState.SetReady(seed > 0);
    }
}
