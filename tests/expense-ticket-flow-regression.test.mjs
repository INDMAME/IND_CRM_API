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
const expenseSheetsControllerSource = readFileSync(
  path.join(repositoryRoot, "Controllers", "CRM", "CrmExpenseSheetsController.cs"),
  "utf8",
);
const createExpenseSheetRequestSource = readFileSync(
  path.join(repositoryRoot, "Contracts", "Requests", "CreateExpenseSheetRequest.cs"),
  "utf8",
);
const updateExpenseSheetHeaderRequestSource = readFileSync(
  path.join(repositoryRoot, "Contracts", "Requests", "UpdateExpenseSheetHeaderRequest.cs"),
  "utf8",
);
const mcpToolsDocument = JSON.parse(
  readFileSync(path.join(repositoryRoot, ".codex", "MCP_TOOLS.json"), "utf8").replace(
    /^\uFEFF/,
    "",
  ),
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
const expenseSheetLineSource = readFileSync(
  path.join(repositoryRoot, ".codex", "Axapta", "CRMHojaGastosLine.xpo"),
  "latin1",
);
const expenseSheetRecalculationJobSource = readFileSync(
  path.join(
    repositoryRoot,
    ".codex",
    "Axapta",
    "INDRecalAmountMSTExchange_HojasGastosV2.xpo",
  ),
  "latin1",
);
const ticketRecalculationJobSource = readFileSync(
  path.join(repositoryRoot, ".codex", "Axapta", "INDRecalcularAmountMST_Tickets.xpo"),
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
const writableHeaderReimbursementValidatorSource = sourceBetween(
  expenseSheetsControllerSource,
  "private static bool IsValidWritableHeaderReimbursableExpense(",
  "private static bool IsValidHeaderReimbursableExpenseFilter(",
);
const headerReimbursementFilterValidatorSource = sourceBetween(
  expenseSheetsControllerSource,
  "private static bool IsValidHeaderReimbursableExpenseFilter(",
  "private static bool IsValidLineReimbursableExpense(",
);
const headerReimbursementFilterNormalizerSource = sourceBetween(
  expenseSheetsControllerSource,
  "private static int? NormalizeReimbursableExpenseOrNull(",
  "private static void AppendExpenseSheetListFilters(",
);
const axWritableHeaderReimbursementValidatorSource = sourceBetween(
  expenseSheetServiceSource,
  "SOURCE #isWritableReimbursableExpense",
  "SOURCE #propagateExpenseSheetCurrencyDefaults",
).replace(/^\s*#/gm, "");
const reimbursableAmountSource = sourceBetween(
  expenseSheetLineSource,
  "SOURCE #recalculateReimbursableAmount",
  "SOURCE #setProjId",
).replace(/^\s*#/gm, "");
const expenseSheetRecalculationJobCode = expenseSheetRecalculationJobSource.replace(
  /^\s*#/gm,
  "",
);
const ticketRecalculationJobCode = ticketRecalculationJobSource.replace(/^\s*#/gm, "");

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

test("header reimbursement writes reject Both while filters keep the derived value", () => {
  const writeValidationCalls =
    expenseSheetsControllerSource.match(
      /!IsValidWritableHeaderReimbursableExpense\(body\.reimbursableExpense\.Value\)/g,
    ) ?? [];

  assert.equal(writeValidationCalls.length, 2);
  assert.match(
    writableHeaderReimbursementValidatorSource,
    /HeaderReimbursableExpenseYesValue[\s\S]*HeaderReimbursableExpenseNoValue/,
  );
  assert.doesNotMatch(
    writableHeaderReimbursementValidatorSource,
    /HeaderReimbursableExpenseBothValue/,
  );
  assert.match(
    headerReimbursementFilterValidatorSource,
    /IsValidWritableHeaderReimbursableExpense\(reimbursableExpense\)[\s\S]*HeaderReimbursableExpenseBothValue/,
  );
  assert.match(
    headerReimbursementFilterNormalizerSource,
    /IsValidHeaderReimbursableExpenseFilter\(reimbursableExpense\.Value\)/,
  );
  assert.match(createExpenseSheetRequestSource, /Only Yes \(0\) and No \(1\) are accepted/);
  assert.match(updateExpenseSheetHeaderRequestSource, /Only Yes \(0\) and No \(1\) are accepted/);

  const createTool = mcpToolsDocument.tools.find(
    ({ name }) => name === "crm_expensesheets_create",
  );
  const updateTool = mcpToolsDocument.tools.find(
    ({ name }) => name === "crm_expensesheets_update_header",
  );
  const listTool = mcpToolsDocument.tools.find(
    ({ name }) => name === "crm_expensesheets_list",
  );

  assert.deepEqual(createTool.inputSchema.properties.body.properties.reimbursableExpense.enum, [0, 1]);
  assert.deepEqual(updateTool.inputSchema.properties.body.properties.reimbursableExpense.enum, [0, 1]);
  assert.deepEqual(listTool.inputSchema.properties.body.properties.reimbursableExpense.enum, [
    0,
    1,
    2,
  ]);
});

test("AX also reserves Both for derived header state", () => {
  const axWriteValidationCalls =
    expenseSheetServiceSource.match(
      /isWritableReimbursableExpense\(reimbursableExpense\)/g,
    ) ?? [];

  assert.equal(axWriteValidationCalls.length, 2);
  assert.match(axWritableHeaderReimbursementValidatorSource, /_value == 0 \|\| _value == 1/);
  assert.doesNotMatch(axWritableHeaderReimbursementValidatorSource, /_value == 2/);
  assert.match(
    expenseSheetServiceSource,
    /isValidReimbursableExpense\(reimbursableExpenseFilter\)/,
  );
  assert.match(
    expenseSheetServiceSource,
    /hasReimbursableExpense\s*&&[\s\S]*isWritableReimbursableExpense\(any2int\(header\.ReimbursableExpense\)\)/,
  );
});

test("reimbursable expense is canonical and Visa remains an inverse legacy mirror", () => {
  assert.match(
    reimbursableAmountSource,
    /if \(this\.ReimbursableExpense == INDReimbursableExpenseLines::Yes\)/,
  );
  assert.match(
    reimbursableAmountSource,
    /this\.VisaEmpresa\s*=\s*NoYes::No;[\s\S]*this\.ReimbursableAmount\s*=\s*this\.AmountMST;/,
  );
  assert.match(
    reimbursableAmountSource,
    /this\.VisaEmpresa\s*=\s*NoYes::Yes;[\s\S]*this\.ReimbursableAmount\s*=\s*0;/,
  );
});

test("legacy Visa values migrate inversely and real zero amounts are persisted", () => {
  assert.match(
    expenseSheetRecalculationJobSource,
    /VisaEmpresa == NoYes::Yes[\s\S]*\? INDReimbursableExpenseLines::No[\s\S]*: INDReimbursableExpenseLines::Yes;/,
  );
  assert.match(
    expenseSheetRecalculationJobSource,
    /if \(newAmountMST == 0 && hojaGastosLineUpdate\.Amount != 0\)/,
  );
});

test("expense migration preserves the legacy company set and isolates each company", () => {
  const activeCompanyBlock = sourceBetween(
    expenseSheetRecalculationJobCode,
    'setCia.add("LAN");',
    '/* Companias excluidas del recorrido heredado.',
  );
  const excludedCompanyBlock = sourceBetween(
    expenseSheetRecalculationJobCode,
    '/* Companias excluidas del recorrido heredado.',
    "*/",
  );
  const companyExecutionBlock = sourceBetween(
    expenseSheetRecalculationJobCode,
    "changeCompany(id)",
    "si.next();",
  );

  assert.deepEqual(
    [...activeCompanyBlock.matchAll(/setCia\.add\("([A-Z]{3})"\);/g)].map(
      ([, companyId]) => companyId,
    ),
    ["LAN", "IHI", "RSI", "SET", "REF", "ISI", "ISM", "AZM", "CUM", "IST", "RIS"],
  );
  assert.deepEqual(
    [...excludedCompanyBlock.matchAll(/setCia\.add\("([A-Z]{3})"\);/g)].map(
      ([, companyId]) => companyId,
    ),
    ["TAZ", "ITA", "ISE"],
  );
  assert.match(
    companyExecutionBlock,
    /accountingRefreshRecIds = new Set\(Types::Integer\);[\s\S]*updateReimbursableAmounts\(\);[\s\S]*updateLines\(\);[\s\S]*updateHeaderReimbursableExpenseStates\(\);[\s\S]*refreshAccountingLines\(\);/,
  );
  assert.doesNotMatch(companyExecutionBlock, /ttsbegin|ttscommit/i);
});

test("ticket recalculation job synchronizes the linked expense line", () => {
  assert.match(
    ticketRecalculationJobSource,
    /ticketInfoUpdate\.doUpdate\(\);[\s\S]*ticketInfoUpdate\.syncHojaGastoLine\(\);/,
  );
});

test("ticket recalculation preserves its legacy multi-company traversal", () => {
  const ticketCompanyBlock = sourceBetween(
    ticketRecalculationJobCode,
    'setCia.add("LAN");',
    "si = new SetIterator(setCia);",
  );
  const ticketCompanyExecutionBlock = sourceBetween(
    ticketRecalculationJobCode,
    "changeCompany(id)",
    "si.next();",
  );

  assert.deepEqual(
    [...ticketCompanyBlock.matchAll(/setCia\.add\("([A-Z]{3})"\);/g)].map(
      ([, companyId]) => companyId,
    ),
    [
      "LAN",
      "IHI",
      "RSI",
      "SET",
      "REF",
      "TAZ",
      "ISI",
      "ISM",
      "AZM",
      "CUM",
      "IST",
      "ITA",
      "ISE",
    ],
  );
  assert.match(ticketCompanyExecutionBlock, /UpdateTickets\(\);/);
  assert.doesNotMatch(ticketCompanyExecutionBlock, /ttsbegin|ttscommit/i);
});
