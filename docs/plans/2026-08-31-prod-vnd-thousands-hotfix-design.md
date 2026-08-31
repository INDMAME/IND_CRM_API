# PROD VND Thousands Hotfix Design

Date: 2026-08-31

## Goal

Allow the deployed Release 11 API to process receipts whose Vietnamese dong total uses grouped thousands, such as `82.000 ₫`, without weakening the existing foreign-currency validation.

## Production Baseline

- Deployed Git commit: `8bd3ce4e848f2fc7b235165a44023c85fa0ad5d8` (`Release 11`).
- Deployed executable SHA-256: `75E157FB585F6C1C21CA033B77077FEDDDAB24B4C7DB0EEB3AB30CFFAAE1BF02`.
- `origin/PROD` points to Release 12 and is not the deployed baseline.
- The hotfix branch must start from the deployed Release 11 commit so deployment does not introduce unrelated Release 12 changes.

## Root Cause

Azure Document Intelligence preserves `82.000 ₫` in `Total.content`, but projects the typed amount as `82`. Release 11 does not recognize the dong symbol and copies that typed amount into the normalization payload. OpenAI returns a valid numeric line with price `82`, and quick-create calculates the ticket total from the lines. The exchange-rate calculation then rounds `82 VND` to `0.00 EUR`, which the API correctly rejects.

## Hotfix Design

1. Map the dong symbol to ISO currency `VND`.
2. Read only the semantic Azure `Total.content` field for the correction.
3. Accept only an integer made of strict three-digit groups when VND evidence is present, for example `82.000`, `82,000`, or `1.234.567`.
4. Remove the grouping separators and verify that the corrected amount is consistent with the structured Azure value by powers of 1000.
5. Carry the corrected amount separately while preserving the shared `TotalAmount` and `PromptJson` with Azure's structured source value.
6. Only for an eligible quick-create receipt with no item breakdown, project the corrected total into the OpenAI JSON and add a prompt rule explaining the already-proven VND grouping semantics.
7. Before quick-create maps the draft to `body.lines`, replace exactly one positive model line with the authoritative corrected VND total. Rejected cases and full-draft keep the original prompt and amount.

The API validation that requires a positive `amountMST` remains unchanged.

## Out of Scope

- No generic multiplication of small foreign-currency values.
- No changes to AX, the web application, exchange-rate providers, or ticket persistence validation.
- No automatic scaling of multiple receipt lines.
- No merge into `PROD` or `DEV` as part of this isolated deployment.

## Tests

The regression script must prove:

- `82.000 ₫` with Azure amount `82` becomes `82000 VND` and one line of `82000`.
- `82,000 VND` and `1.234.567 ₫` normalize correctly.
- An already correct VND amount remains unchanged.
- EUR and USD decimal formats remain unchanged.
- `82.000` without VND evidence is not corrected.
- Ambiguous VND formats and multi-line drafts are not scaled automatically.
- The normal expense-ticket regression suite and Release x86 build still pass.

## Deployment and Rollback

Before deployment, preserve the current published directory and its SHA-256 manifest. Build and test from the isolated hotfix worktree, then deploy with the repository reinstall script. Validate the Windows service, TCP port 7776, anonymous health endpoint, and compiled/published hashes.

If validation or the ticket smoke test fails, reinstall the preserved Release 11 artifact and repeat all service and hash checks.

## DEV Forward Port

DEV must receive an equivalent implementation adapted to its current OCR reconciliation code. The forward port must retain the same VND evidence requirements and tests, and must not merge or publish automatically.
