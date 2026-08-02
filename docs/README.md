# MTP runner coverage channels — working notes

Supporting material for a discussion about the MTP test runner's coverage and identity channels. Nothing
here is a proposal to adopt; the RFC explicitly asks for disagreement.

| document | what it is |
|---|---|
| [`defect-census.md`](defect-census.md) | Nineteen defects across three channels, ranked by how likely you are to meet one — only one is reached by an ordinary project on a released build. Line-level receipt and upstream status for each; a runnable test for seven. Start here. |
| [`rfc-mtp-coverage-channels.md`](rfc-mtp-coverage-channels.md) | Why the defects recur, what any replacement has to satisfy, one design that satisfies it, the decisions that need maintainer input, and eleven designs that were tried and refuted. |
| [`mmap-survive-check/`](mmap-survive-check) | A twenty-line experiment behind one of the RFC's load-bearing claims, with a platform result matrix. Self-contained — clone, `dotnet run`, done. |

The defects are independent of the design discussion: six of the nineteen are one-to-five-line fixes with
failing tests already written, and they stand on their own.
