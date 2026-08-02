# MTP runner coverage channels — working notes

Supporting material for a discussion about the MTP test runner's coverage and identity channels. Nothing
here is a proposal to adopt; the RFC explicitly asks for disagreement.

| document | what it is |
|---|---|
| [`defect-census.md`](defect-census.md) | Nineteen defects across three channels, each with a line-level receipt, upstream status, and — for eight of them — a test you can run. Start here. |
| [`rfc-mtp-coverage-channels.md`](rfc-mtp-coverage-channels.md) | Why the defects recur, what any replacement has to satisfy, one design that satisfies it, the decisions that need maintainer input, and eleven designs that were tried and refuted. |
| [`mmap-survive-check/`](mmap-survive-check) | A twenty-line experiment behind one of the RFC's load-bearing claims, with a platform result matrix. Two commands to run it yourself. |

The defects are independent of the design discussion: six of the nineteen are one-to-five-line fixes with
failing tests already written, and they stand on their own.
