# DEV VND Thousands Forward-Port Prompt

Use this prompt in the current IND_CRM_API DEV checkout after the isolated PROD hotfix has been validated.

```text
Implement the validated VND grouped-thousands correction in IND_CRM_API DEV.

Safety and branch boundaries:

- Start from the current remote DEV branch in a clean, isolated worktree and create a dedicated feature branch.
- Inspect the current DEV implementation before editing. DEV may contain newer receipt reconciliation code than Release 11, so adapt the behavior instead of blindly cherry-picking the PROD commit.
- Do not merge, deploy, publish, or modify PROD.
- Keep the existing validation that rejects amountMST values less than or equal to zero. Do not introduce a value of 1 or any other manufactured financial fallback.

Confirmed root cause:

- Azure Document Intelligence preserves a Vietnamese dong total such as `82.000 U+20AB` in `documents[0].fields.Total.content` but can project `valueCurrency.amount` as `82`.
- The downstream draft then contains a line for `82 VND`; conversion to company currency rounds to `0.00 EUR`, and the API correctly rejects it.

Required behavior:

1. Recognize Unicode U+20AB as VND.
2. Read correction evidence only from the exact first semantic `documents[0].fields.Total` field. Do not scan generic receipt text, OCR pages, or later documents to authorize scaling.
3. Accept local VND evidence only when `Total.content` contains U+20AB or a standalone VND token, or when that same Total field has structured currency code VND. Reject any conflicting structured or content currency.
4. Accept only a positive integer written with strict repeated three-digit grouping using one separator: examples `82.000`, `82,000`, and `1.234.567`. Reject mixed separators, decimals, signs, leading zeros, spaces, apostrophes, extra numeric text, and overflow.
5. Parse the grouped integer exactly and prove it equals the structured Total amount multiplied by 1000^k, where k is positive and no greater than the number of grouping separators. Do not use tolerances, logarithms, reverse ratios, or heuristics. If k is zero, the structured value was already correct and needs no correction marker.
6. Carry both the corrected total and its original structured source amount through the OCR analysis result. Keep the shared `TotalAmount` and shared `PromptJson` on Azure's original structured source value.
7. Only after the deterministic correction is proven, and only for QuickCreate with `ItemCount == 0`, create a profile-specific OpenAI JSON that projects the corrected VND total and include the VND grouping rule in that request's prompt. FullDraft and every rejected case must receive the original prompt JSON and no VND correction rule.
8. Apply the deterministic draft correction only for QuickCreate, when Azure returned no item breakdown, the model produced exactly one positive line, quantity is exactly 1, and its price equals either the structured source amount or the already-corrected candidate. Set currency to VND and price to the corrected total. Do not collapse or scale multiple lines.
9. Preserve DEV's existing total reconciliation behavior, including any `Total Paid` exclusions and `ReconcileDraftTotalFromOcr` logic. Integrate the correction at the narrowest boundary without weakening those protections.

Regression requirements:

- Prove the pre-fix DEV binary fails the representative `82.000 U+20AB` / structured `82` case before accepting the implementation.
- Positive cases: `82.000 U+20AB` -> `82000 VND`; `82,000 VND` -> `82000`; `1.234.567 U+20AB` with the exact power-of-1000 source relationship -> `1234567`; an already-correct structured amount remains unchanged.
- Negative cases: EUR and USD decimals, no local VND evidence, conflicting currency, evidence only elsewhere in OCR text, invalid first document followed by a valid one, mixed separators, decimal suffix, sign, leading zero, space grouping, ratio mismatch, itemized receipt, quantity other than 1, unrelated model amount, multiple lines, and non-QuickCreate profile.
- Assert the final normalized QuickCreate JSON contains currencyCode VND, quantity 1, and price 82000.
- Run the repository regression suite, compile Release x86 with zero errors, and report existing warnings separately.

Before handing off, provide the branch and commit, exact changed files, red/green evidence, build results, and a concise risk assessment. Stop before merge or deployment.
```
