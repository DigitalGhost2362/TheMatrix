# RAM observations

Goal: find how many clones this machine runs safely and which steps make RAM grow.
`[auto]` lines are low-RAM alerts from the sampler and the `up` RAM gate. Raw samples are in
`ram-samples.csv` (time, total, available, load %, original editor, clone count, clones total, per clone
working set / peak).

## Safe clone count found so far

| Date | Machine RAM | Clones | Peak per clone | Free at peak | Verdict |
|---|---|---|---|---|---|

## Entries

```
### <yyyy-MM-dd HH:mm> - <who> - <clone> - <run id>
- What happened:
- Evidence: (numbers)
- RAM at the time: free <MB>, clone <MB> / peak <MB>
- Workaround used:
- Suggested Matrix fix:
```

---
