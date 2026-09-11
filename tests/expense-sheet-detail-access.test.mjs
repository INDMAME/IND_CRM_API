import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const controllerUrl = new URL("../Controllers/CRM/CrmExpenseSheetsController.cs", import.meta.url);
const serviceXpoUrl = new URL("../.codex/Axapta/INDCRMExpenseSheetService.xpo", import.meta.url);

const extractMethod = (source, startMarker, endMarker) => {
  const start = source.indexOf(startMarker);
  const end = source.indexOf(endMarker, start);
  assert.notEqual(start, -1, `Missing start marker: ${startMarker}`);
  assert.notEqual(end, -1, `Missing end marker: ${endMarker}`);
  return source.slice(start, end);
};

test("expense sheet detail derives its actor from the signed snapshot", async () => {
  const source = await readFile(controllerUrl, "utf8");
  const action = extractMethod(
    source,
    "public IHttpActionResult GetExpenseSheet(string hojaGastosId)",
    "public IHttpActionResult UpdateExpenseSheetHeader"
  );

  assert.match(action, /RequireValidatedSnapshotAxUserIdOrReturn403/u);
  assert.doesNotMatch(action, /RequireAxUserIdOrReturn422/u);
  assert.match(action, /con\.Append\(viewerAxUserId\);[\s\S]*con\.Append\(hojaGastosId\.Trim\(\)\);[\s\S]*con\.Append\(1\);/u);
});

test("AX exact lookup authorizes only the owner or an existing managed owner", async () => {
  const source = await readFile(serviceXpoUrl, "latin1");
  const method = extractMethod(source, "SOURCE #getExpenseSheet", "SOURCE #updateExpenseSheetHeader");

  assert.match(method, /if \(conLen\(_data\) >= 4\)[\s\S]*allowManagedOwner = any2int\(conPeek\(_data, 4\)\) == 1/u);
  assert.match(method, /where header\.HojaGastosId == hojaId/u);
  assert.match(method, /header\.UserId != crmUserId[\s\S]*canExpenseSheetActorManageOwner\(axUserId, header\.UserId\)/u);
  assert.match(method, /else[\s\S]*where header\.UserId\s+== crmUserId[\s\S]*header\.HojaGastosId == hojaId/u);
  assert.match(method, /where line\.UserId\s+== header\.UserId/u);
  assert.doesNotMatch(method, /where line\.UserId\s+== crmUserId/u);
});
