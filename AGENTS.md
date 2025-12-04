# IND_CRM_API agent profile

## Tech constraints

- Project target: .NET Framework 4.8, Web API 2, OWIN self host.
- Platform MUST stay x86 because of AxaptaCOMConnector.
- Backend ERP is Navision Axapta 3.0 via Business Connector COM.
- Do NOT migrate this project to .NET Core or change framework version.

## Design goals

- Keep the API stable while improving structure, readability, and safety.
- Apply clean code principles: small focused methods, clear names, no duplication.
- Keep the current REST endpoints behavior unless there is a clear bug.
- Treat Axapta integration as a critical dependency: never break COM calls.

## Swagger / OpenAPI

- Add and configure Swagger / OpenAPI using Swashbuckle for Web API 2 (.NET 4.8).
- Do NOT use Swashbuckle.AspNetCore or ASP.NET Core packages.
- Expose a stable OpenAPI document for all CRM endpoints so other projects can generate typed clients.
- Do not rename routes unless strictly necessary; prefer documenting and annotating them.

## Axapta COM integration

- Axapta session manager must remain safe for x86 and Business Connector COM.
- Wrap COM interactions in defensive code (try/catch, logging) without changing business logic.
- Never introduce multi threading that may break the COM client.
- If you refactor COM wrappers, keep the public surface compatible or clearly explain changes.

## Documentation and style

- All comments and docstrings must be in simple English without accents or special characters.
- Avoid any non ASCII characters in code or comments.
- Add XML documentation on public controllers, services, and key Axapta integration classes.
- Explain purpose, inputs, outputs, and error cases in one or two short English sentences.

## Working style

- Before large edits, summarize your understanding of the current structure and propose a short plan.
- Prefer incremental refactors that compile at each step.
- Never add new heavy dependencies without an explicit comment explaining why they are needed.
- Keep configuration for .NET 4.8 x86 and AxaptaCOMConnector untouched unless explicitly asked to change it.