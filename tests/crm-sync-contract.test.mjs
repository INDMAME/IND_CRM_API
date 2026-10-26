import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

// Source-level contract checks of the CRM_INSERTEC sync receiver: routes, identity boundary,
// container layout parity between C# and X++, XPO format and mirror parity. No AX or build needed.

const repo = new URL("../", import.meta.url);
const read = (path, encoding = "utf8") => readFile(new URL(path, repo), encoding);

const extractMethod = (source, startMarker, endMarker) => {
  const start = source.indexOf(startMarker);
  assert.notEqual(start, -1, `Missing start marker: ${startMarker}`);
  const end = endMarker ? source.indexOf(endMarker, start) : source.length;
  assert.notEqual(end, -1, `Missing end marker: ${endMarker}`);
  return source.slice(start, end);
};

// X++ lines inside an XPO SOURCE block carry a "#" prefix; strip it to match plain X++ patterns.
const extractXpp = (xpo, methodName) =>
  extractMethod(xpo, `SOURCE #${methodName}`, "ENDSOURCE").replace(/^\s*#/gmu, "");

test("sync controller exposes the documented routes without the Entra context gates", async () => {
  const controller = await read("Controllers/CRM/CrmSyncController.cs");
  const base = await read("Controllers/CRM/BaseSyncController.cs");

  assert.match(controller, /\[RoutePrefix\("api\/crm\/sync"\)\]/u);
  for (const route of ['Route("ping")', 'Route("visits/upsert")', 'Route("visits/delete")', 'Route("prospects/upsert")',
    'Route("contacts/upsert")', 'Route("visits/{recId:int}/version")', 'Route("visits/{recId:int}")']) {
    assert.ok(controller.includes(route), `route missing: ${route}`);
  }

  assert.match(base, /\[SyncWorkerAuthorize\]/u);
  assert.match(base, /GetHeaderValue\(Request, "X-IND-Company"\)/u);
  for (const forbidden of ["RequireValidatedSnapshotAxUserIdOrReturn403", "UserContextTokenService", "X-IND-EntraOid", "X-IND-AxUserId"]) {
    assert.ok(!controller.includes(forbidden) && !base.includes(forbidden), `sync endpoints must not use ${forbidden}`);
  }
});

test("login tokens carry the sync-worker role only through SyncWorkerIdentity", async () => {
  const jwt = await read("Services/JwtService.cs");
  const auth = await read("Controllers/System/AuthController.cs");
  const attribute = await read("App_Start/SyncWorkerAuthorizeAttribute.cs");

  assert.match(jwt, /SyncWorkerIdentity\.RolesFor\(username\)/u);
  assert.match(jwt, /new Claim\(SyncWorkerIdentity\.RoleClaimType, role\.Trim\(\)\)/u);
  assert.match(auth, /_jwt\.GenerateToken\(dto\.Username, expirationMinutes\)/u);
  assert.match(attribute, /SyncWorkerIdentity\.IsSyncWorkerPrincipal\(principal\)/u);
  assert.match(attribute, /IndErrorCodes\.SyncWorkerForbidden/u);
});

test("C# container layout matches the X++ receiver positions", async () => {
  const service = await read("Services/CrmSyncService.cs");
  const xpo = await read(".codex/Axapta/INDCrmSyncService.xpo", "latin1");
  const upsertVisit = extractXpp(xpo, "upsertVisit");

  // C# appends envelope (1-4), axRecId (5), base version (6-8) and the fields up to webCode (21) and attendeeCount (22).
  const csharpUpsert = extractMethod(service, "public SyncResultDto UpsertVisit", "public SyncResultDto DeleteVisit");
  const appends = csharpUpsert.match(/con\.Append\(|AppendEnvelope\(|AppendBaseVersion\(|AppendAttendees\(/gu) ?? [];
  // AppendEnvelope = 4 values, AppendBaseVersion = 3 values, AppendAttendees = count + rows.
  assert.equal(appends.length, 1 + 1 + 1 + 1 + (21 - 8), "upsertVisit appends 21 fixed values plus the attendee block");
  assert.match(upsertVisit, /webCode\s+= conPeek\(_data, 21\);/u);
  assert.match(upsertVisit, /attendeeCount\s+= conPeek\(_data, 22\);/u);
  assert.match(upsertVisit, /pos = 23 \+ i \* 6;/u);

  const deleteVisit = extractXpp(xpo, "deleteVisit");
  assert.match(deleteVisit, /axRecId\s+= conPeek\(_data, 5\);/u);
  const upsertProspect = extractXpp(xpo, "upsertProspect");
  assert.match(upsertProspect, /responsibleCrmUserId = conPeek\(_data, 14\);/u);
  const upsertContact = extractXpp(xpo, "upsertContact");
  assert.match(upsertContact, /phone\s+= conPeek\(_data, 11\);/u);

  // Output header read by C#: 13 positions, attendees in the second element.
  assert.match(service, /Detail = NullIfEmpty\(Str\(row, 10\)\)/u);
  assert.match(service, /SafePeekContainer\(root, 2\)/u);
  const buildResult = extractXpp(xpo, "buildResult");
  assert.match(buildResult, /return \[header, _attendees\];/u);
});

test("X++ receiver keeps deduplication and mutation in one transaction and never merges divergences", async () => {
  const xpo = await read(".codex/Axapta/INDCrmSyncService.xpo", "latin1");
  for (const name of ["ping", "upsertVisit", "deleteVisit", "upsertProspect", "upsertContact", "getVisitVersion", "getVisit",
    "applyAttendees", "attendeeDivergence", "writeMap"]) {
    assert.ok(xpo.includes(`SOURCE #${name}`), `X++ method missing: ${name}`);
  }

  const upsertVisit = extractXpp(xpo, "upsertVisit");
  assert.match(upsertVisit, /ttsbegin;[\s\S]*INDCrmSyncMap::find\(externalId, INDCrmSyncEntityType::Visit, true\)/u);
  assert.match(upsertVisit, /map\.LastMessageId == messageId[\s\S]*outcome = "duplicate"/u);
  assert.match(upsertVisit, /map\.LastVersion > version[\s\S]*SYNC_VERSION_BEHIND/u);
  assert.match(upsertVisit, /activity\.modifiedDate != baseDate \|\| activity\.modifiedTime != baseTime[\s\S]*outcome = "divergent"/u);
  assert.match(upsertVisit, /INDCrmSyncService::writeMap\(externalId, INDCrmSyncEntityType::Visit[\s\S]*ttscommit;/u);
  assert.match(upsertVisit, /else\s*\{\s*ttsabort;/u);
  assert.match(upsertVisit, /activity\.Status\s+= CRMActivityStatus::Finalizada;/u);
  assert.match(upsertVisit, /activity\.INDCreatedByUserId = ownerAxUserId;/u);

  const prospect = extractXpp(xpo, "upsertProspect");
  assert.match(prospect, /CustParameters::numRefCRMClientePotencialId\(\)/u);
  assert.match(prospect, /prospect\.validateWrite\(\)/u);
  assert.match(prospect, /buildResult\(outcome, account\.RecId, prospect\.ClientePotencialId/u);
});

test("new XPO files are Windows-1252, CRLF, without BOM, balanced and mirrored byte for byte", async () => {
  for (const name of ["INDCrmSyncEntityType", "INDCrmSyncMap", "INDCrmSyncService", "INDCrmSyncMapForm"]) {
    const api = await readFile(new URL(`.codex/Axapta/${name}.xpo`, repo));
    const app = await readFile(new URL(`../IND_CRM_APP/.codex/Axapta/${name}.xpo`, repo));
    assert.ok(api.equals(app), `${name}.xpo differs between API and APP`);
    assert.ok(!(api[0] === 0xef && api[1] === 0xbb && api[2] === 0xbf), `${name}.xpo has a UTF-8 BOM`);

    const text = api.toString("latin1");
    const lines = text.split("\n");
    for (const line of lines.slice(0, -1)) assert.ok(line.endsWith("\r"), `${name}.xpo has an LF-only line`);
    const opens = (text.match(/^\s*SOURCE #/gmu) ?? []).length;
    const closes = (text.match(/^\s*ENDSOURCE/gmu) ?? []).length;
    assert.equal(opens, closes, `${name}.xpo has unbalanced SOURCE/ENDSOURCE blocks`);
    assert.match(text, /\*\*\*Element: END\r\n$/u);
  }
});

test("ENDPOINTS.md documents every sync route and the worker identity", async () => {
  const endpoints = await read(".codex/ENDPOINTS.md");
  const section = extractMethod(endpoints, "## Sincronización CRM_INSERTEC", "## Plantilla interna");
  for (const route of ["GET /api/crm/sync/ping", "POST /api/crm/sync/visits/upsert", "POST /api/crm/sync/visits/delete",
    "POST /api/crm/sync/prospects/upsert", "POST /api/crm/sync/contacts/upsert", "GET /api/crm/sync/visits/{recId}/version",
    "GET /api/crm/sync/visits/{recId}"]) {
    assert.ok(section.includes(route), `ENDPOINTS.md misses ${route}`);
  }
  assert.match(section, /ind:role=sync-worker/u);
  assert.match(section, /INDCRM_SYNC_USER/u);
});
