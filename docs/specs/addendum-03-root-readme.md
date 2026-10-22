# Addendum 3: Root `README.md`

**Status: Implemented.**

Like Addenda 1 and 2, not called for by the original design spec — the spec and its milestones
cover the tool itself, not repo-facing documentation. Added on direct request: the repo had
`CLAUDE.md` (internals, aimed at AI sessions working in the codebase) but nothing human-facing for
someone just looking at the project.

## What changed

- **`README.md`** (repo root, new): project overview, feature highlights, install steps
  (`--install` vs. the one-off `--init powershell | Out-String | Invoke-Expression`, matching the
  corrected pattern from `addendum-02-stdout-discipline-bugfixes.md`), a short usage table (full
  reference deferred to `gt --help`), a "how it works" section (native exe + PowerShell wrapper
  split, TSV data files, link to `docs/specs/00-design-spec.md`), build-from-source instructions,
  and a third-party license note for the bundled Everything SDK DLL.
- Deliberately kept short and example-driven rather than duplicating `CLAUDE.md`'s file-by-file
  layout table or internal design-decision notes — those stay in `CLAUDE.md`, which this file links
  to for anyone who wants the deeper tour.

## Key files

- `README.md` (new)
