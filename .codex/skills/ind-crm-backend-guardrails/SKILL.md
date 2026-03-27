---
name: ind-crm-backend-guardrails
description: Use when creating or modifying backend APIs, Axapta integrations, contracts, documentation, or production configuration in IND_CRM_API.
---

# IND CRM Backend Guardrails

## Mission
This skill is the local authority for backend work in IND_CRM_API.
Apply it before changing API code, Axapta contracts, endpoint documentation, or environment-dependent configuration.
The repo is already in production, so the default stance is conservative: understand first, change the minimum necessary, and preserve compatibility unless the user explicitly asks otherwise.

## Non-negotiable rules
- Keep the project on .NET Framework 4.8 and x86. Do not propose migrations that break Axapta COM or `AxaptaSessionManager`.
- Existing production or already published endpoints must not be removed, unregistered, or dropped from the project unless the responsible user explicitly asks for it.
- Do not create test projects, `Tests/` folders, or add test frameworks unless the user explicitly requests tests in that turn.
- Do not perform broad rewrites, cross-cutting refactors, response-envelope migrations, or contract redesigns just because an old document mentions them. Only do them when the current task requires them or the user asks for them.
- Never hardcode secrets, passwords, tokens, tenant ids, company ids, Ax user ids, connection strings, model keys, or environment URLs in code or docs.
- If a new credential or secret is required, route it through the existing system configuration path used by the project so DEV and PROD stay interoperable. If no secure path exists, stop and ask before coding.

## Context loading policy
Load the minimum relevant context instead of reading all `.codex` documents by default.

Source order:
1. This skill.
2. `.codex/ENDPOINTS.md` for current HTTP contracts, required headers, date formats, and routing notes.
3. `.codex/MCP_ENDPOINTS.md` and `.codex/MCP_TOOLS.json` only when MCP exposure or tool schemas are part of the task.
4. `.codex/POSTMAN.md` and `.codex/Postman/POSTMAN_VERSIONING.md` only when Postman collections, environments, or versioning are part of the task.
5. Relevant `.codex/AX_*_CHANGES_*.md` files only for the class or module being touched. Treat them as historical implementation logs, not as universal rules.
6. `.codex/AGENTS.md` only as secondary background. It contains still-valid guardrails, but also historical implementation prompts and outdated references, so it must never be the sole source of truth for current versions or current backlog.
7. `.codex/skills/ind-crm-backend-guardrails/references/*.md` are mirror copies for portability. Do not load them when the root `.codex` file has already been read.

Interpretation rules:
- If documents disagree, prefer the most specific and current operational source.
- For HTTP contracts and required headers, prefer `ENDPOINTS.md`.
- For MCP tool shapes, prefer `MCP_TOOLS.json` first and `MCP_ENDPOINTS.md` as narrative support.
- For Postman lineage, prefer `POSTMAN_VERSIONING.md` plus the actual `DEV` and `PROD` folders, not old version references embedded in other docs.
- Treat `Pendientes`, one-off implementation objectives, or historical rollout notes as non-binding unless they are confirmed by current code or current canonical docs.

## Planning gate before code
Before editing code:
- Analyze the current flow and identify the exact modules, contracts, and boundaries affected.
- Present a short plan in bullets for any non-trivial change.
- Propose the smallest safe change that solves the request.
- If there are multiple valid approaches, present concise options with a recommendation and ask before implementing.
- If requirements are unclear or behavior could change in different valid ways, ask a clarifying question before coding.
- Only proceed on assumption when the assumption is low-risk, backward-compatible, and explicitly called out.

## Conservative implementation rules
- Prefer surgical edits over rewrites.
- Apply clean architecture before coding: clarify controller, DTO, validation, service, mapper, integration, and shared-component responsibilities first.
- Respect logical module boundaries. Do not leak Axapta concerns across controllers or duplicate mapping and validation logic across endpoints.
- If the same logic is being touched in two or more places, prefer extracting or extending a shared helper, mapper, validator, or service. If the reuse is speculative, do not create a new abstraction.
- Refactor only enough to support the requested change safely and clearly.
- Keep public contracts stable unless the change is explicitly requested and documented.
- When a new feature touches multiple endpoints or modules, prefer a small shared standard/component over copy-paste, but keep the blast radius narrow.

## API rules that remain mandatory
- In every endpoint creation or modification, perform a routing review before closing the work.
- Routing checklist:
  - Verify collisions between literal and parameterized routes.
  - Verify uniqueness of `HTTP method + route template`.
  - Add route constraints when ambiguity is possible.
  - Review `RoutePrefix`, sibling routes, and legacy conventional routing.
  - Validate potentially conflicting endpoints in Postman when applicable.
  - Confirm the request reaches the expected controller/action in diagnostics when logs exist.
- If routing is ambiguous, fix it before continuing.
- In `tickets` and `hojas de gastos`, request dates must accept `DDMMYYYY` and `DD.MM.YYYY`.
- In `tickets` and `hojas de gastos`, response dates must be normalized to `DD.MM.YYYY`.
- The conversion boundary is API input/output vs Axapta internal format. Do not leak raw AX date formats to clients.
- Required business headers stay aligned with current endpoint documentation:
  - `X-IND-Company` for CRM business endpoints.
  - `X-IND-AxUserId` whenever the endpoint sends user identity to AX.
- If an API contract changes, update the relevant docs and annotations with the same level of precision as the code change.

## Axapta workflow
Apply this only when a task touches an Axapta class or AX-bound contract.

Before editing:
- Analyze the class and method flow first.
- Identify input/output container indices, validations, compatibility constraints, and side effects.
- When the change has design choices, propose at least two options when that adds value.

Execution:
1. Create a class-scoped plan limited to the AX class first.
2. Create or update `.codex/AX_<ClassName>_CHANGES_YYYY-MM-DD.md`.
3. Keep that file current with objective, methods touched, contract adjustments, risks, and pending API work.
4. Implement AX first when the contract originates there, then align DTOs, mappers, endpoints, and docs.
5. Do not close AX->API work if the temporary change log is stale or incomplete.

## Documentation hygiene
- Do not duplicate root `.codex` docs into new parallel files unless there is a strong reason.
- Prefer updating the canonical document for the affected area instead of scattering new notes.
- Historical AX change logs are acceptable as audit trail for active work, but they are not canonical API references.
- If a document is clearly historical or obsolete, treat it as context only and do not let it drive new design by itself.

## Verification gate
Before finishing:
- Confirm the change is backward-compatible or explicitly approved.
- Confirm no secret or environment-specific value was hardcoded.
- Confirm routing review was completed for API changes.
- Confirm any affected canonical documentation was updated.
- Validate changes with the normal compile/run flow used by this repo.
- State in the final summary which checks were performed and any residual risk.

## Supporting skills
Use the minimum applicable subset of supporting skills. Do not fan out to every backend skill by default.

Recommended order when the task needs them:
1. `brainstorming` for non-trivial design or behavior changes.
2. `rest-api-expert` or `rest-api-design` for route, contract, and HTTP semantics.
3. `api-design-patterns` when versioning, auth, or error-evolution patterns matter.
4. `backend-architect` when boundaries, rollout shape, or shared standards must be decided.
5. `dotnet-framework-4.8-expert` for implementation details in this stack.
6. `api-documenter` when contract docs or Swagger/OpenAPI must change.
7. `code-review` for final verification or review-specific tasks.

## Precedence
1. `ind-crm-backend-guardrails`
2. Current canonical `.codex` documents relevant to the task
3. Current code and actual project structure
4. Supporting skills
5. General best practices
