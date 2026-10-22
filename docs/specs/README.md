# goto specs

- [`00-design-spec.md`](./00-design-spec.md) — the full original design spec, kept verbatim as the
  source of truth for architecture, algorithms, file formats, and the CLI contract.
- `milestone-0N-*.md` — one file per build-order milestone (spec §12). Each one says which parts of
  the full spec it covers, what's explicitly out of scope (deferred to a later milestone), its
  acceptance criteria, and (once implemented) the specific decisions made that weren't already
  pinned down by the spec.

| Milestone | File | Status |
|---|---|---|
| 1 | [milestone-01-skeleton.md](./milestone-01-skeleton.md) | Implemented |
| 2 | [milestone-02-walker-resolver-decisions.md](./milestone-02-walker-resolver-decisions.md) | Implemented |
| 3 | [milestone-03-picker-and-too-many.md](./milestone-03-picker-and-too-many.md) | Implemented |
| 4 | [milestone-04-everything-backend.md](./milestone-04-everything-backend.md) | Implemented |
| 5 | [milestone-05-ranking-frecency-explain.md](./milestone-05-ranking-frecency-explain.md) | Implemented |
| 6 | [milestone-06-extras.md](./milestone-06-extras.md) | Implemented |
| 7 | [milestone-07-polish.md](./milestone-07-polish.md) | Implemented |

Update a milestone's status and implementation notes when it ships, the same way
`milestone-01-skeleton.md` documents Milestone 1.
