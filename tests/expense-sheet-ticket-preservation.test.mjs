import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import test from "node:test";

const root = path.resolve(import.meta.dirname, "..");
const service = readFileSync(path.join(root, ".codex/Axapta/INDCRMExpenseSheetService.xpo"), "latin1");
const lineTable = readFileSync(path.join(root, ".codex/Axapta/CRMHojaGastosLine.xpo"), "latin1");
const controller = readFileSync(path.join(root, "Controllers/CRM/CrmExpenseSheetsController.cs"), "utf8");

// Extracts the real AX method without changing its Windows-1252 representation.
function method(name) {
  const match = new RegExp(`SOURCE #${name}\\r?\\n([\\s\\S]*?)ENDSOURCE`).exec(service);
  assert.ok(match, `Missing AX method: ${name}`);
  return match[1].replace(/^\s*#/gm, "");
}

// AX delete actions follow FileId, independently of the API cleanup inventory.
test("line deletion breaks the ticket cascade before the kernel deletes the expense line", () => {
  assert.match(lineTable, /DELETEACTIONS\s+#INDTicketInfoTable[\s\S]*?DeleteAction\s+#Cascade/);
  const unlink = method("deleteExpenseLinePreservingTicket");
  assert.match(unlink, /if\s*\(_line\.FileId\)\s*\{\s*_line\.FileId\s*=\s*'';\s*_line\.doUpdate\(\);\s*\}/);
  assert.ok(unlink.indexOf("_line.doUpdate();") < unlink.indexOf("_line.delete();"));
  assert.doesNotMatch(unlink, /skipDeleteActions|doDelete\(|INDTicketInfoTable.*delete|deleteAzureBlob/);

  const deletion = method("deleteExpenseSheetLine");
  const releases = [...deletion.matchAll(/fileIdToRelease = line\.FileId;\s*INDCRMExpenseSheetService::deleteExpenseLinePreservingTicket\(line\);\s*if \(fileIdToRelease\)\s*INDCRMExpenseSheetService::refreshTicketStatusByFileId\(axUserId, fileIdToRelease\);/g)];
  assert.equal(releases.length, 2, "Both whole-sheet and individual-line deletion must preserve the linked ticket");
  assert.doesNotMatch(deletion, /\bline\.delete\(\)/);
  assert.ok(deletion.indexOf("ttsbegin;") < deletion.indexOf("deleteExpenseLinePreservingTicket(line)"));
  assert.ok(deletion.lastIndexOf("ttscommit;") > deletion.lastIndexOf("deleteExpenseLinePreservingTicket(line)"));
});

// Executes the actual helper body against a fake AX cascade keyed by persisted FileId.
test("unlinking keeps ticket lines and files while deleting expense-line children", () => {
  const source = method("deleteExpenseLinePreservingTicket");
  const execute = new Function("_line", source.slice(source.indexOf("{") + 1, source.lastIndexOf("}")));
  for (const createdFromTicket of [false, true]) {
    let persistedFileId = "TICKET1";
    let expenseLineExists = true;
    let expenseChildrenExist = true;
    const ticket = { exists: true, linesExist: true, blobExists: true };
    execute({ FileId: "TICKET1", createdFromTicket,
      doUpdate() { persistedFileId = this.FileId; },
      delete() {
        if (persistedFileId === "TICKET1") {
          ticket.exists = ticket.linesExist = ticket.blobExists = false;
        }
        expenseLineExists = expenseChildrenExist = false;
      },
    });
    assert.equal(expenseLineExists, false);
    assert.equal(expenseChildrenExist, false, "Other expense-line delete actions must still execute");
    assert.deepEqual(ticket, { exists: true, linesExist: true, blobExists: true }, `Origin: ${createdFromTicket}`);
  }
});

// A failure to persist the broken relation must stop before any delete action executes.
test("failed unlinking cannot fall through to a destructive delete", () => {
  const source = method("deleteExpenseLinePreservingTicket");
  const execute = new Function("_line", source.slice(source.indexOf("{") + 1, source.lastIndexOf("}")));
  let deleted = false;
  assert.throws(() => execute({ FileId: "TICKET1", doUpdate() { throw Error("Update failed"); },
    delete() { deleted = true; } }), /Update failed/);
  assert.equal(deleted, false);
});

// A missing AX entry point must fail before the old destructive method can execute.
test("durable deletion requires the AX preservation entry point without a legacy fallback", () => {
  const entry = method("deleteExpenseSheetLinePreservingTickets");
  assert.match(entry, /return INDCRMExpenseSheetService::deleteExpenseSheetLine\(_data\);/);
  const start = controller.indexOf("internal IHttpActionResult DeleteExpenseSheetForCleanup");
  const end = controller.indexOf("private IHttpActionResult DeleteExpenseSheetLineCore", start);
  const cleanup = controller.slice(start, end);
  assert.equal((cleanup.match(/requireTicketPreservation: true/g) || []).length, 2,
    "Both complete and older line inventories require the safe AX entry point");
  assert.match(controller, /requireTicketPreservation\s*\?\s*"deleteExpenseSheetLinePreservingTickets"\s*:\s*"deleteExpenseSheetLine"/);
});
