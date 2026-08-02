using System.Diagnostics;
using System.IO.MemoryMappedFiles;

// Does a store made through a file-backed memory mapping survive a force-kill, and stay visible to a
// different process, with no flush, no dispose and no exit handler?
//
//   dotnet run -c Release -- write  <path>     writes bytes 1..10, then kills itself
//   dotnet run -c Release -- verify <path>     separate process: reads the file, reports PASS/FAIL
//
// The write step is expected to terminate abnormally - that is the point of the test.

var mode = args.Length > 0 ? args[0] : "";
var path = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "mmap-survive.bin");

if (mode == "write")
{
    using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
    stream.SetLength(64);
    using var mmf = MemoryMappedFile.CreateFromFile(
        stream, null, 64, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: true);
    using var view = mmf.CreateViewAccessor(0, 64, MemoryMappedFileAccess.ReadWrite);

    for (byte i = 1; i <= 10; i++)
    {
        view.Write(i, i);          // "mutant i was covered"
    }

    Console.WriteLine($"pid {Environment.ProcessId}: wrote 1..10 to {path}");
    Console.WriteLine("killing self now - no Flush(), no Dispose(), no ProcessExit handler");
    Console.Out.Flush();
    Process.GetCurrentProcess().Kill();   // SIGKILL on Unix, TerminateProcess on Windows
    Thread.Sleep(10_000);                 // unreachable if the kill took effect
    Console.WriteLine("ERROR: still alive - the kill did not take effect");
    return 2;
}

if (mode == "verify")
{
    if (!File.Exists(path))
    {
        Console.WriteLine($"FAIL: {path} does not exist - did the write step run?");
        return 1;
    }

    byte[] bytes;
    for (var attempt = 0; ; attempt++)     // a dead writer's handle can linger a moment on Windows
    {
        try { bytes = File.ReadAllBytes(path); break; }
        catch (IOException) when (attempt < 50) { Thread.Sleep(100); }
    }

    var actual = bytes.Take(12).ToArray();
    var expected = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 0 };
    var pass = actual.SequenceEqual(expected);

    Console.WriteLine($"expected: {string.Join(",", expected)}");
    Console.WriteLine($"actual:   {string.Join(",", actual)}");
    Console.WriteLine(pass
        ? "PASS - mapped stores survived the force-kill and are visible to another process"
        : "FAIL - mapped stores did NOT survive; this platform needs an explicit publish step");
    return pass ? 0 : 1;
}

Console.WriteLine("usage: dotnet run -c Release -- write|verify [path]");
return 64;
