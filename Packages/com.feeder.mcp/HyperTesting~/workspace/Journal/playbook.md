# Play-test playbook

The orchestrator reads this before every Hyper Testing session and updates it after.

## Best known flow

1. Read this file and the last entries of the other journal files.
2. Memory pre-flight (skill, "Memory pre-flight"): original compiled and NOT in play mode, heavy scenes closed,
   `UnloadUnusedAssetsImmediate` + `GC.Collect`, original restarted if its commit is above 6000 MB or its Mono
   heap above 2048 MB, no Gradle build running, commit left >= one clone budget + `ramReserveMb`.
3. `hyper-testing` `status` -> `up` (count from the safe clone count in `ram-observations.md`, default 1 until one
   is recorded) -> poll `status` until ready; read its `WARN` lines.
4. `refresh` -> every clone `compiled clean`.
5. Write and split test cases; probe one `matrix-hcN` tool, then spawn all `hyper-playtester-N` in one message.
6. Poll `status` every few minutes while they run; watch commit and `CRITICAL` lines.
7. Summary, fixes in the original, `refresh`, re-run only failures.
8. Journal, `down` (read its `CRITICAL` lines).

## Timings measured

| Date | Clones | New clone prepare | First open | Refresh+compile per clone | Cases | Wall time | One-by-one estimate |
|---|---|---|---|---|---|---|---|

## Tips proven to work

-

## Candidates (not yet proven - playtesters add here)

-
