# Validation & QA

`generatedts` uses a combination of automated tests and TypeScript compilation to
guarantee output quality.

## Unit tests

`dotnet test` executes tests included in the repository (if present).  These
cover mapping logic and other small utilities.  Always run the test suite before
submitting changes.

## Full validation script

`node Scripts/validate.js` performs an end-to-end sanity check:

1. Recreates `/.tests/validation/` (clean slate).
2. Generates declarations + metadata for the configured BCL assembly list (see
   `Scripts/validate.js` for the exact set).
3. Writes `index.d.ts` with triple-slash references to every generated file.
4. Emits a `tsconfig.json` targeting ES2020 with `strict` settings and no emit.
5. Invokes `tsc --project` on the validation directory.

The script reports:

- Syntax errors (TS1xxx) – must always be zero.
- Semantic errors (TS2xxx) – should trend toward zero; any increases must be
  labelled “expected” in the script output.
- Duplicate identifier warnings (TS6200) – the generator intentionally defines
  branded numeric types once per assembly; these are tracked but acceptable.

The script exits with a non-zero code if any syntax errors occur or generation
fails, ensuring CI catches regressions.

## Manual checklist for new contributions

1. `dotnet test`
2. `node Scripts/validate.js` (outside sandbox, so the script can write to
   `.tests/validation/`)
3. Inspect diff for `.d.ts`/`.metadata.json` to confirm expected transform.
4. Update documentation under `spec/` when adding new rules.

Following this process keeps the generated output stable and ensures TS tooling
continues to accept the declarations.
