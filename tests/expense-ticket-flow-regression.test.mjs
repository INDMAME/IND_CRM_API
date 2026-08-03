import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const currentDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(currentDirectory, "..");

const controllerSource = readFileSync(
  path.join(repositoryRoot, "Controllers", "CRM", "CrmExpenseSheetTicketsController.cs"),
  "utf8",
);
const errorCodesSource = readFileSync(
  path.join(repositoryRoot, "Models", "Responses", "INDErrorCodes.cs"),
  "utf8",
);
const ticketTableSource = readFileSync(
  path.join(repositoryRoot, ".codex", "Axapta", "INDTicketInfoTable.xpo"),
  "latin1",
);
const expenseSheetServiceSource = readFileSync(
  path.join(repositoryRoot, ".codex", "Axapta", "INDCRMExpenseSheetService.xpo"),
  "latin1",
);

// Extracts one source unit so each assertion stays scoped to the intended method.
function sourceBetween(source, startMarker, endMarker) {
  const start = source.indexOf(startMarker);
  assert.notEqual(start, -1, `Missing source marker: ${startMarker}`);

  const end = source.indexOf(endMarker, start + startMarker.length);
  assert.notEqual(end, -1, `Missing source marker: ${endMarker}`);
  return source.slice(start, end);
}

const duplicateValidationSource = sourceBetween(
  ticketTableSource,
  "SOURCE #validateUniqueTicketDateTime",
  "SOURCE #validateWrite",
).replace(/^\s*#/gm, "");
const axTicketCreateSource = sourceBetween(
  expenseSheetServiceSource,
  "SOURCE #createExpenseSheetTicket",
  "SOURCE #createExpenseSheetTicketLine",
).replace(/^\s*#/gm, "");
const provisionalCreateSource = sourceBetween(
  controllerSource,
  "private bool TryCreateQuickCreateProvisionalTicket(",
  "private bool TryUploadQuickCreateTicketFile(",
);
const rollbackSource = sourceBetween(
  controllerSource,
  "private bool TryRollbackQuickCreatePartialTicket(",
  "private static IndApiResponse<ExpenseSheetTicketQuickCreateResultDto> BuildQuickCreateErrorResponse(",
);
const ticketErrorSource = sourceBetween(
  controllerSource,
  "private static IndApiResponse<object> BuildTicketActionError(",
  "private static IndApiResponse<object> BuildExpenseSheetActionError(",
);
const quickCreateSource = sourceBetween(
  controllerSource,
  "public async Task<IHttpActionResult> QuickCreateExpenseSheetTicket(",
  "[HttpGet, Route(\"{fileId}\")]",
);

function hasTicketDateTimeConflict(candidate, existing) {
  if (!candidate.ticketDate || !candidate.ticketTime) {
    return false;
  }

  const sameTicketDateTime =
    candidate.ticketDate === existing.ticketDate &&
    candidate.ticketTime === existing.ticketTime &&
    candidate.recId !== existing.recId;
  const sameOwner = candidate.createdByUserId
    ? candidate.createdByUserId === existing.createdByUserId
    : true;
  return sameTicketDateTime && sameOwner;
}

function rollbackSucceeds({ blobDeleteAttempted, blobDeleted, ticketDeleted }) {
  const blobSucceeded = !blobDeleteAttempted || blobDeleted;
  return blobSucceeded && ticketDeleted;
}

test("same day tickets without a detected time remain distinct", () => {
  assert.match(
    duplicateValidationSource,
    /if \(!this\.TicketDate \|\| !this\.TicketTime\)[\s\S]*return true;/,
  );
  assert.equal(
    hasTicketDateTimeConflict(
      { ticketDate: "20260803", ticketTime: 0, recId: 2, createdByUserId: "USER1" },
      { ticketDate: "20260803", ticketTime: 0, recId: 1, createdByUserId: "USER1" },
    ),
    false,
  );
});

test("a real duplicate for the same owner is rejected", () => {
  assert.match(duplicateValidationSource, /ticketInfoTable\.TicketDate == this\.TicketDate/);
  assert.match(duplicateValidationSource, /ticketInfoTable\.TicketTime == this\.TicketTime/);
  assert.match(duplicateValidationSource, /ticketInfoTable\.RecId\s*!=\s*this\.RecId/);
  assert.equal(
    hasTicketDateTimeConflict(
      { ticketDate: "20260803", ticketTime: 36000, recId: 2, createdByUserId: "USER1" },
      { ticketDate: "20260803", ticketTime: 36000, recId: 1, createdByUserId: "USER1" },
    ),
    true,
  );
});

test("the same date and time remain valid for different owners", () => {
  assert.match(
    duplicateValidationSource,
    /if \(this\.CreatedByUserId\)[\s\S]*ticketInfoTable\.CreatedByUserId == this\.CreatedByUserId/,
  );
  assert.equal(
    hasTicketDateTimeConflict(
      { ticketDate: "20260803", ticketTime: 36000, recId: 2, createdByUserId: "USER2" },
      { ticketDate: "20260803", ticketTime: 36000, recId: 1, createdByUserId: "USER1" },
    ),
    false,
  );
});

test("blank AX owner preserves the legacy global duplicate fallback", () => {
  const duplicateSelects = duplicateValidationSource.match(/select firstonly RecId, FileId/g) ?? [];
  assert.equal(duplicateSelects.length, 2);
  assert.equal(
    hasTicketDateTimeConflict(
      { ticketDate: "20260803", ticketTime: 36000, recId: 2, createdByUserId: "" },
      { ticketDate: "20260803", ticketTime: 36000, recId: 1, createdByUserId: "USER1" },
    ),
    true,
  );
});

test("provisional quick-create leaves TicketDate empty until OCR", () => {
  assert.match(provisionalCreateSource, /NormalizeApiDateToAxYmd\(body\?\.ticketDate\)/);
  assert.doesNotMatch(provisionalCreateSource, /NormalizeTicketDateToAxYmdOrFallback/);
  assert.match(axTicketCreateSource, /hasTicketDate\s*=\s*false;/);
  assert.match(
    axTicketCreateSource,
    /if \(hasTicketDate && ticketDate\)[\s\S]*ticketHeader\.TicketDate\s*=\s*ticketDate;/,
  );
  assert.doesNotMatch(axTicketCreateSource, /ticketHeader\.TicketDate\s*=\s*transDate/);
});

test("duplicate ticket conflicts return HTTP 409 and a stable error code", () => {
  assert.match(
    errorCodesSource,
    /CrmExpenseSheetTicketDuplicate\s*=\s*"CRM_EXPENSESHEET_TICKET_DUPLICATE"/,
  );
  assert.match(ticketErrorSource, /status = HttpStatusCode\.Conflict/);
  assert.match(ticketErrorSource, /ErrorCode = IndErrorCodes\.CrmExpenseSheetTicketDuplicate/);
});

test("rollback fails when an attempted blob deletion returns false", () => {
  assert.match(rollbackSource, /var blobSucceeded = !blobDeleteAttempted \|\| blobDeleted;/);
  assert.match(rollbackSource, /var rollbackSucceeded = blobSucceeded && ticketDeleted;/);
  assert.equal(
    rollbackSucceeds({ blobDeleteAttempted: true, blobDeleted: false, ticketDeleted: true }),
    false,
  );
});

test("sheet linking rolls back on failure and records completed success", () => {
  assert.match(
    quickCreateSource,
    /if \(!TryLinkTicketToExpenseSheet\([\s\S]*RollbackQuickCreateIfNeeded\("sheet-link", linkMessage\);/,
  );
  assert.match(quickCreateSource, /resultData\.LinkedToSheet = true;/);
  assert.match(quickCreateSource, /resultData\.CompletedStage = QuickCreateStageSheetLinked;/);
});
