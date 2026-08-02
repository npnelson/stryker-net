# Does mapped coverage survive a force-kill?

A design that writes coverage into a file-backed memory mapping and never explicitly publishes it rests on
one OS behaviour: **dirty pages of a file-backed mapping belong to the page cache, so they survive a
force-kill and are readable by another process without any flush.** This checks that, on whatever platform
you run it.

Needs only the .NET SDK. Two files, no dependencies.

## Run it

```bash
git clone --branch docs/mtp-channel-rfc --single-branch --depth 1 https://github.com/npnelson/stryker-net.git mtp-rfc
cd mtp-rfc/docs/mmap-survive-check
dotnet run -c Release -- write     # writes bytes 1..10 through the mapping, then kills itself
dotnet run -c Release -- verify    # separate process: reads the file, prints PASS or FAIL
```

Identical on Windows, Linux and macOS — PowerShell, cmd or a shell all work. Pass an explicit path as a
second argument if the temp directory is awkward:

```powershell
dotnet run -c Release -- write  C:\Temp\mmap-survive.bin
dotnet run -c Release -- verify C:\Temp\mmap-survive.bin
```

The `write` step **is supposed to die abnormally** — it calls `Kill()` on itself, so it disposes nothing,
flushes nothing and runs no exit handler. Its exit code is platform-specific (137 on Unix, a large value on
Windows) and is *not* the evidence. The evidence is what `verify` reads back from a different process.

Expected where this works:

```
expected: 0,1,2,3,4,5,6,7,8,9,10,0
actual:   0,1,2,3,4,5,6,7,8,9,10,0
PASS - mapped stores survived the force-kill and are visible to another process
```

`verify` exits 0 on PASS, 1 on FAIL.

## Results

| platform | result |
|---|---|
| Linux — Ubuntu 24.04, .NET 10, x64 | **PASS** |
| Windows 11, .NET 10, x64 | **PASS** |
| macOS | not yet run |
| ARM64 (any OS) | not yet run |

Windows was the one most likely to differ: it reaches the file through the cache manager rather than the
POSIX page cache, and `TerminateProcess` semantics around a mapped section differ from `SIGKILL`. It
passes, so the "write in place, never publish" rule holds on both major platforms rather than being
Linux-specific. macOS and ARM64 remain open — ARM64 for its weaker memory ordering, which is a different
question from durability and matters more for any design that also relies on cross-process *ordering*.

## What this does and does not prove

It proves a **completed** store stays visible after the writing process dies without cleanup.

It does **not** prove the test finished, that every store a producer intended actually happened, or that
the producer finished initialising. A force-kill can preserve positive evidence; it can never manufacture
complete negative evidence. "No coverage recorded" after a kill means *unknown*, not *empty* — which is why
a seal is still required, and why this experiment is necessary but nowhere near sufficient.
