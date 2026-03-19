# Questions For User Input (Assumptions Applied)

1. Header length mismatch:
- Question: Spec text says a 40-byte header, but listed fields (4 + 2 + 32) and payload offset both indicate 38 bytes. Which should be canonical long-term?
- Assumption used now: Implemented 38-byte header to match field sizes and payload slicing requirement (`data[38..]`).

2. Task tracking file location:
- Question: Initial instructions referenced `./design/todo.md`, but the repository contains root `./todo.md`. Should task status always be tracked in root `todo.md`?
- Assumption used now: Continued using root `todo.md` and marked prompts complete there.

3. Rename UX behavior:
- Question: Should rename ask the user for custom text or use deterministic auto-rename?
- Assumption used now: Implemented deterministic file rename by appending `_renamed` (with numeric suffix collision handling).

4. Export warning logging sink:
- Question: Where should export quality clamp warnings be persisted (file, ETW, telemetry, etc.)?
- Assumption used now: Used `System.Diagnostics.Trace.TraceWarning`.

5. Local build verification:
- Question: `dotnet` is unavailable in this environment. Can you provide a machine/runner with .NET SDK for compile/test verification?
- Assumption used now: Implemented all tasks and performed static consistency checks only; runtime compile/test pending.

6. Target framework version:
- Question: Should we keep strict .NET 9 targeting or stay on .NET 10 for this environment?
- Assumption used now: Retargeted projects to 
et10.0-windows so build/test/run work with installed SDK/runtime (10.0.201 / 10.0.5).

