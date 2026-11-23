import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import { readdir, readFile } from "node:fs/promises";
import test from "node:test";

// Source-level contract checks of the web store receiver (api/crm/webstore/*): published routes,
// own authorization boundary, no trace of the previous receiver, and parity of the container
// positions between C# (CrmWebStoreContainerBuilder), the specification (CRM_INSERTEC
// docs/RECEPTOR_CRMWEB.md, section 5) and the X++ classes CRMWeb* in .codex/Axapta.
// No AX, COM or build needed.

const repo = new URL("../", import.meta.url);
// Text files are compared with LF line breaks whatever the checkout uses.
const read = async (path) => (await readFile(new URL(path, repo), "utf8")).replace(/\r\n/gu, "\n");
const specUrl = new URL("../CRM_INSERTEC/docs/RECEPTOR_CRMWEB.md", repo);

// Expected layouts (specification of 2026-11-19, section 5). Position = index + 1.
const ENVELOPE = ["company", "messageId", "externalId", "version"];
const EXPECTED = {
  visitUpsert: [...ENVELOPE, "axRecId", "activity.accountNum", "activity.origin", "activity.custAccount",
    "activity.originCustAccount", "activity.refRecId", "activity.name", "activity.country", "activity.userId",
    "activity.createdByUserId", "activity.division", "activity.transDate", "activity.realDate", "activity.activityType",
    "activity.status", "activity.priority", "activity.visitType", "activity.contactMethod", "activity.rating",
    "activity.description", "activity.comments", "activity.background", "activity.conclusions", "attendees.length"],
  attendee: ["externalId", "axRecId", "type", "name", "position", "contactRecId", "createdByUserId"],
  visitDelete: [...ENVELOPE, "axRecId"],
  contactUpsert: [...ENVELOPE, "axRecId", "contact.accountNum", "contact.origin", "contact.name", "contact.position",
    "contact.email", "contact.phone", "contact.company", "contact.companyPhone"],
  prospectUpsert: [...ENVELOPE, "axRecId", "prospect.name", "prospect.legalName", "prospect.city", "prospect.countryId",
    "prospect.statisticsGroup", "prospect.contactOrigin", "prospect.vatNum", "prospect.userId"],
};

const RESULT_HEADER = ["result", "recId", "code", "modifiedDate", "modifiedTime", "errorCode", "errorMessage", "company", "accountRecId"];

const slice = (source, startMarker, endMarker) => {
  const start = source.indexOf(startMarker);
  assert.notEqual(start, -1, `Missing start marker: ${startMarker}`);
  const end = endMarker ? source.indexOf(endMarker, start + startMarker.length) : source.length;
  assert.notEqual(end, -1, `Missing end marker: ${endMarker}`);
  return source.slice(start, end);
};

// Ordered JSON paths appended by one CrmWebStoreContainerBuilder method.
const csharpLayout = (builder, method) => {
  const body = slice(builder, `public static List<object> ${method}(`, "\n        }\n");
  const prefixes = { m: "", a: "activity.", c: "contact.", p: "prospect.", t: "" };
  const layout = [];
  for (const line of body.split("\n")) {
    if (/=\s*Envelope\(company, m\)/u.test(line)) {
      layout.push(...ENVELOPE);
      continue;
    }
    if (/values\.Add\(m\.attendees\.Count\)/u.test(line)) {
      layout.push("attendees.length");
      continue;
    }
    const match = line.match(/values\.Add\(\w+\(([amcpt])\.(\w+)\)\)/u);
    if (match) layout.push(`${match[1] === "t" ? "attendee." : prefixes[match[1]]}${match[2]}`);
  }
  return layout;
};

// Ordered JSON paths of the specification tables (section 5.x), or null when the spec is not present.
const specLayouts = async () => {
  if (!existsSync(specUrl)) return null;
  const spec = (await readFile(specUrl, "utf8")).replace(/\r\n/gu, "\n");
  const envelope = slice(spec, "Sobre común", "\n");
  const envelopeNames = [...envelope.matchAll(/`(\w+)`/gu)].map((m) => m[1]);
  const table = (heading) => {
    const section = slice(spec, heading, "\n### ");
    return [...section.matchAll(/^\| (\d+) \| `([^`]+)` \|/gmu)].map((m) => ({ pos: Number(m[1]), name: m[2] }));
  };
  const ordered = (rows) => {
    rows.forEach((row, i) => assert.equal(row.pos, 5 + i, `spec table position ${row.pos} out of order`));
    return [...envelopeNames, ...rows.map((row) => row.name)];
  };
  const attendeeLine = slice(spec, "Asistente, en este orden:", "\n");
  const deleteLine = slice(spec, "### 5.2", "\n### ");
  return {
    visitUpsert: ordered(table("### 5.1")),
    // JSON names follow ": " or ", "; AX field names follow an arrow.
    attendee: [...attendeeLine.matchAll(/(?:: |, )`(\w+)`/gu)].map((m) => m[1]),
    visitDelete: [...envelopeNames, ...[...deleteLine.matchAll(/Posición (\d+): `(\w+)`/gu)].map((m) => m[2])],
    contactUpsert: ordered(table("### 5.3")),
    prospectUpsert: ordered(table("### 5.4")),
    resultHeader: slice(slice(spec, "### 5.5", "\n## "), "Container: `[[", "]").replace("Container: `[[", "").split(",").map((s) => s.trim()),
  };
};

// X++ text of one method of an XPO (lines carry a "#" prefix in the XPO SOURCE block).
const xppMethod = (xpo, name) => slice(xpo, `SOURCE #${name}`, "ENDSOURCE").replace(/^\s*#/gmu, "");

// Container positions read with conPeek(<var>, N) or conPeek(<var>, #Macro) in an X++ text.
const peekedPositions = (xpp) => {
  const macros = Object.fromEntries([...xpp.matchAll(/#define\.(\w+)\((\d+)\)/gu)].map((m) => [m[1], Number(m[2])]));
  const positions = new Set();
  for (const m of xpp.matchAll(/conPeek\(\s*[\w.]+\s*,\s*(#?\w+)\s*\)/gu)) {
    const arg = m[1];
    if (/^\d+$/u.test(arg)) positions.add(Number(arg));
    else if (arg.startsWith("#") && macros[arg.slice(1)] !== undefined) positions.add(macros[arg.slice(1)]);
  }
  return positions;
};

const readXpo = async (name) => {
  const url = new URL(`.codex/Axapta/${name}.xpo`, repo);
  assert.ok(existsSync(url), `XPO pendiente: falta .codex/Axapta/${name}.xpo; la paridad C#/X++ no se puede comprobar hasta que exista.`);
  return readFile(url, "latin1");
};

const listCs = async (dir) => {
  const entries = await readdir(new URL(dir, repo), { withFileTypes: true, recursive: true });
  return entries.filter((e) => e.isFile() && e.name.endsWith(".cs"))
    .map((e) => `${(e.parentPath ?? e.path).replace(/\\/gu, "/")}/${e.name}`);
};

test("web store controllers publish the documented routes", async () => {
  const controller = await read("Controllers/CRM/CrmWebStoreController.cs");
  const auth = await read("Controllers/CRM/CrmWebStoreAuthController.cs");

  assert.match(controller, /\[RoutePrefix\("api\/crm\/webstore"\)\]/u);
  assert.match(controller, /\[HttpGet, Route\("ping"\)\]/u);
  for (const route of ["visits/upsert", "visits/delete", "contacts/upsert", "prospects/upsert"]) {
    assert.ok(controller.includes(`[HttpPost, Route("${route}")]`), `route missing: POST ${route}`);
  }
  assert.match(auth, /\[RoutePrefix\("api\/crm\/webstore\/auth"\)\]/u);
  assert.match(auth, /\[HttpPost, Route\("login"\)\]/u);

  const csproj = await read("IND_CRM_API.csproj");
  for (const file of ["App_Start\\CrmWebStoreAuthorizeAttribute.cs", "Controllers\\CRM\\CrmWebStoreController.cs",
    "Controllers\\CRM\\CrmWebStoreAuthController.cs", "Services\\CrmWebStoreService.cs", "Services\\CrmWebStoreTokenService.cs",
    "Services\\Interfaces\\ICrmWebStoreService.cs"]) {
    assert.ok(csproj.includes(`<Compile Include="${file}" />`), `csproj misses ${file}`);
  }
});

test("web store routes use only their own authorization attribute and no legacy identity", async () => {
  const controller = await read("Controllers/CRM/CrmWebStoreController.cs");
  const auth = await read("Controllers/CRM/CrmWebStoreAuthController.cs");
  const attribute = await read("App_Start/CrmWebStoreAuthorizeAttribute.cs");

  assert.match(controller, /\[CrmWebStoreAuthorize\]\s*\[RoutePrefix/u);
  assert.match(controller, /class CrmWebStoreController : ApiController/u);
  assert.doesNotMatch(controller, /\[Authorize\]|BaseCrmController|AllowAnonymous/u);
  for (const forbidden of ["RequireValidatedSnapshotAxUserIdOrReturn403", "UserContextTokenService", "X-IND-EntraOid",
    "X-IND-AxUserId", "IJwtService", "JwtService"]) {
    assert.ok(!controller.includes(forbidden), `web store controller must not use ${forbidden}`);
  }

  // The login is anonymous by design and never issues a legacy token.
  assert.match(auth, /\[AllowAnonymous\]/u);
  assert.doesNotMatch(auth, /\[Authorize\]|IJwtService|GenerateToken\(/u);
  assert.match(auth, /_tokens\.IsConfiguredUser\(requested\)[\s\S]*_sessionManager\.CreateOrGetSession\(username, body\.password, null\)/u,
    "the user is checked before the credentials are sent to Axapta");
  assert.doesNotMatch(auth, /Log\([^;]*password=/u, "the password is never logged");

  // The attribute reads the bearer itself and ignores the principal of the global middleware.
  assert.match(attribute, /class CrmWebStoreAuthorizeAttribute : AuthorizeAttribute/u);
  assert.match(attribute, /public override void OnAuthorization/u);
  assert.match(attribute, /_tokens\.Validate\(ReadBearer\(actionContext\.Request\)\)/u);
  assert.doesNotMatch(attribute, /base\.(OnAuthorization|IsAuthorized)/u);

  // No other controller uses the web store attribute, and the web store controller has no other.
  for (const file of await listCs("Controllers/")) {
    if (file.endsWith("CrmWebStoreController.cs")) continue;
    const source = await readFile(file, "utf8");
    assert.ok(!source.includes("[CrmWebStoreAuthorize"), `${file} must not use CrmWebStoreAuthorize`);
  }
});

test("token service is separate from JwtService and reads AX_WEBSTORE_USER (machine variable before App.config)", async () => {
  const tokens = await read("Services/CrmWebStoreTokenService.cs");
  assert.match(tokens, /AppSettingsHelper\.GetSetting\(UserSettingKey, UserVariableName\)/u);
  assert.match(tokens, /UserVariableName = "AX_WEBSTORE_USER"/u);
  assert.match(tokens, /UserSettingKey = "CrmWebStore:User"/u);
  assert.match(tokens, /Audience = "IND_CRM_WEBSTORE"/u);
  assert.match(tokens, /new HMACSHA256\(/u, "the signing key is derived, not the raw legacy secret");
  assert.match(tokens, /ValidateIssuer = true,[\s\S]*ValidateAudience = true,[\s\S]*ValidateLifetime = true,[\s\S]*ValidateIssuerSigningKey = true/u);
  assert.doesNotMatch(tokens, /new JwtService|IJwtService|GenerateToken\(|INDCRM_JWT_AUDIENCE|INDCRM_JWT_ISSUER/u);

  // App.config only gains the user key; its value is the machine variable placeholder, never a real user.
  const appConfig = await read("App.config");
  const webStoreLines = appConfig.split("\n").filter((line) => /webstore|WebStore|AX_WEBSTORE/u.test(line) && !/^\s*<!--/u.test(line));
  assert.deepEqual(webStoreLines.map((line) => line.trim()), ['<add key="CrmWebStore:User" value="%AX_WEBSTORE_USER%" />']);
  assert.doesNotMatch(appConfig, /SyncWorker|sync-worker|INDCRM_SYNC/u, "App.config keeps a trace of the previous receiver");

  for (const script of ["scripts/set-indcrm-machine-env.ps1", "scripts/set-indcrm-machine-all-env.ps1"]) {
    assert.match(await read(script), /-Name "AX_WEBSTORE_USER"/u, `${script} does not offer AX_WEBSTORE_USER`);
  }
});

test("shared login and pipeline files carry nothing of the web store or of the previous receiver", async () => {
  for (const file of ["Services/JwtService.cs", "Services/Interfaces/IJwtService.cs", "Controllers/System/AuthController.cs",
    "Startup.cs", "App_Start/TokenRefreshHandler.cs", "App_Start/DependencyConfig.cs", "App_Start/CompanyHeaderOperationFilter.cs",
    "Models/Responses/INDErrorCodes.cs", "WebApiConfig.cs"]) {
    const source = await read(file);
    assert.doesNotMatch(source, /webstore|WebStore|AX_WEBSTORE/u, `${file} must not mention the web store`);
    assert.doesNotMatch(source, /SyncWorker|sync-worker|CrmSync|api\/crm\/sync|INDCRM_SYNC|SYNC_/u, `${file} keeps a trace of the previous receiver`);
  }
});

test("the previous receiver is gone from code, tests, scripts, collections and living docs", async () => {
  for (const removed of ["Controllers/CRM/CrmSyncController.cs", "Controllers/CRM/BaseSyncController.cs", "Services/CrmSyncService.cs",
    "Services/Interfaces/ICrmSyncService.cs", "App_Start/SyncWorkerAuthorizeAttribute.cs", "Helpers/SyncWorkerIdentity.cs",
    "Helpers/AxSyncDateTime.cs", "Contracts/Requests/SyncRequests.cs", "Contracts/Responses/SyncDtos.cs",
    "tests/crm-sync-contract.test.mjs", "tests/sync-worker-identity.test.ps1", "tests/fixtures/sync-worker-identity.cs.fixture"]) {
    assert.ok(!existsSync(new URL(removed, repo)), `${removed} must be removed`);
  }

  const pattern = /SyncWorker|sync-worker|CrmSync|api\/crm\/sync|INDCRM_SYNC_USER/u;
  const files = [
    ...(await listCs("App_Start/")), ...(await listCs("Controllers/")), ...(await listCs("Services/")),
    ...(await listCs("Helpers/")), ...(await listCs("Contracts/")), ...(await listCs("Models/")),
  ];
  for (const file of files) assert.doesNotMatch(await readFile(file, "utf8"), pattern, `${file} references the previous receiver`);
  for (const file of ["IND_CRM_API.csproj", "package.json", "scripts/set-indcrm-machine-env.ps1", "scripts/set-indcrm-machine-all-env.ps1",
    ".codex/ENDPOINTS.md", ".codex/TECH_SPECS.md", ".codex/PROJECT_STRUCTURE.md", ".codex/MCP_ENDPOINTS.md", ".codex/POSTMAN.md"]) {
    assert.doesNotMatch(await read(file), pattern, `${file} references the previous receiver`);
  }
  for (const dir of [".codex/postman/", "Notes/"]) {
    const entries = await readdir(new URL(dir, repo), { withFileTypes: true, recursive: true });
    for (const entry of entries.filter((e) => e.isFile() && e.name.endsWith(".json"))) {
      const path = `${(entry.parentPath ?? entry.path).replace(/\\/gu, "/")}/${entry.name}`;
      assert.doesNotMatch(await readFile(path, "utf8"), /api\/crm\/sync|sync-worker/u, `${path} references the previous receiver`);
    }
  }
});

test("C# container layout follows the specification", async () => {
  const builder = await read("Services/CrmWebStoreContainerBuilder.cs");
  const csharp = {
    visitUpsert: csharpLayout(builder, "BuildVisitUpsert"),
    visitDelete: csharpLayout(builder, "BuildVisitDelete"),
    contactUpsert: csharpLayout(builder, "BuildContactUpsert"),
    prospectUpsert: csharpLayout(builder, "BuildProspectUpsert"),
  };
  const attendee = csharp.visitUpsert.filter((name) => name.startsWith("attendee.")).map((name) => name.slice("attendee.".length));
  csharp.visitUpsert = csharp.visitUpsert.filter((name) => !name.startsWith("attendee."));

  for (const key of ["visitUpsert", "visitDelete", "contactUpsert", "prospectUpsert"]) {
    assert.deepEqual(csharp[key], EXPECTED[key], `C# ${key} positions differ from the specification`);
  }
  assert.deepEqual(attendee, EXPECTED.attendee, "C# attendee values differ from the specification");
  assert.match(builder, /VisitAttendeeCountPosition = 28;/u);
  assert.match(builder, /VisitFirstAttendeePosition = 29;/u);
  assert.match(builder, /AttendeeValueCount = 7;/u);

  const reader = await read("Services/CrmWebStoreResultReader.cs");
  RESULT_HEADER.forEach((name, i) => {
    const constant = `${name[0].toUpperCase()}${name.slice(1)}Position = ${i + 1};`;
    assert.ok(reader.includes(constant), `result reader misses ${constant}`);
  });
  assert.match(reader, /Attendees = ReadAttendees\(At\(root, 2\)\)/u);
});

test("embedded layouts match the specification document", async (t) => {
  const spec = await specLayouts();
  if (!spec) {
    t.skip("CRM_INSERTEC/docs/RECEPTOR_CRMWEB.md is not next to this repository");
    return;
  }
  for (const key of ["visitUpsert", "attendee", "visitDelete", "contactUpsert", "prospectUpsert"]) {
    assert.deepEqual(spec[key], EXPECTED[key], `specification ${key} changed; update C# and this test together`);
  }
  assert.deepEqual(spec.resultHeader, RESULT_HEADER, "specification result header changed");
});

test("X++ store classes read the positions that C# writes", async () => {
  const common = await readXpo("CRMWebStoreCommon");
  const visit = await readXpo("CRMWebVisitStore");
  const contact = await readXpo("CRMWebContactStore");
  const prospect = await readXpo("CRMWebProspectStore");

  for (const [xpo, name, methods] of [[common, "CRMWebStoreCommon", ["ping"]], [visit, "CRMWebVisitStore", ["upsert", "remove"]],
    [contact, "CRMWebContactStore", ["upsert"]], [prospect, "CRMWebProspectStore", ["upsert"]]]) {
    for (const method of methods) assert.ok(xpo.includes(`SOURCE #${method}`), `${name} misses the static method ${method}`);
  }
  for (const code of ["STORE_MESSAGE_INVALID", "STORE_RECORD_NOT_FOUND", "STORE_VERSION_BEHIND", "STORE_AX_EXCEPTION"]) {
    assert.ok([common, visit, contact, prospect].some((xpo) => xpo.includes(code)), `no CRMWeb* class returns ${code}`);
  }

  // The envelope (1-4) may be read by a helper of CRMWebStoreCommon; each store reads its own positions.
  const envelope = peekedPositions(common.replace(/^\s*#/gmu, ""));
  const all = (xpo) => new Set([...envelope, ...peekedPositions(xpo.replace(/^\s*#/gmu, ""))]);
  const expectRange = (positions, from, to, label) => {
    for (let pos = from; pos <= to; pos++) assert.ok(positions.has(pos), `${label}: X++ does not read container position ${pos}`);
    const beyond = [...positions].filter((pos) => pos > to);
    assert.deepEqual(beyond, [], `${label}: X++ reads fixed positions beyond ${to}: ${beyond.join(", ")}`);
  };
  const visitPositions = all(visit);
  expectRange(visitPositions, 1, EXPECTED.visitUpsert.length, "CRMWebVisitStore");
  expectRange(all(contact), 1, EXPECTED.contactUpsert.length, "CRMWebContactStore");
  expectRange(all(prospect), 1, EXPECTED.prospectUpsert.length, "CRMWebProspectStore");
  assert.ok(peekedPositions(xppMethod(visit, "remove")).has(5) || /remove[\s\S]*conPeek\(\s*[\w.]+\s*,\s*5\s*\)/u.test(visit),
    "CRMWebVisitStore.remove does not read axRecId at position 5");

  // Attendee block: first value at 29 and seven values per attendee.
  const visitXpp = visit.replace(/^\s*#/gmu, "");
  assert.match(visitXpp, /\b29\b/u, "CRMWebVisitStore does not use 29 as the first attendee position");
  assert.match(visitXpp, /\b7\b/u, "CRMWebVisitStore does not use 7 values per attendee");
});

test("ENDPOINTS.md documents the web store routes and access", async () => {
  const endpoints = await read(".codex/ENDPOINTS.md");
  const section = slice(endpoints, "## Receptor CRMWeb (CRM_INSERTEC)", "\n## ");
  for (const route of ["POST /api/crm/webstore/auth/login", "GET /api/crm/webstore/ping", "POST /api/crm/webstore/visits/upsert",
    "POST /api/crm/webstore/visits/delete", "POST /api/crm/webstore/contacts/upsert", "POST /api/crm/webstore/prospects/upsert"]) {
    assert.ok(section.includes(route), `ENDPOINTS.md misses ${route}`);
  }
  assert.match(section, /AX_WEBSTORE_USER/u);
  for (const object of ["CRMWebStoreCommon", "CRMWebVisitStore", "CRMWebContactStore", "CRMWebProspectStore", "CRMWebRecordGuard",
    "CRMWebSyncMap", "WebLocked"]) {
    assert.ok(section.includes(object), `ENDPOINTS.md misses ${object}`);
  }
  assert.doesNotMatch(endpoints, /## Sincronización CRM_INSERTEC/u, "the previous receiver section must be gone");
});
