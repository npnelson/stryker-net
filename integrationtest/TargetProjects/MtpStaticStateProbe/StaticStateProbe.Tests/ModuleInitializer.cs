using System.Runtime.CompilerServices;
using StaticStateProbe;

namespace StaticStateProbe.Tests;

internal static class ModuleInitializer
{
    /// <summary>
    /// Runs once, at test-host start, before any test and before the runner can change the active
    /// mutant. Whatever mutant is active when a host starts is therefore baked into
    /// <see cref="StartupState"/> for that host's entire lifetime.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize() => _ = new StartupConfigurator(1);
}
