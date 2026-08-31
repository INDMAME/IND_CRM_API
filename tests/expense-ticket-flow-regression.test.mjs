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
const updateExpenseSheetLineRequestSource = readFileSync(
  path.join(repositoryRoot, "Contracts", "Requests", "UpdateExpenseSheetLineRequest.cs"),
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
const expenseSheetHeaderSource = readFileSync(
  path.join(repositoryRoot, ".codex", "Axapta", "CRMHojaGastosTable.xpo"),
  "latin1",
);
const purchParametersSource = readFileSync(
  path.join(repositoryRoot, ".codex", "Axapta", "PurchParameters.xpo"),
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
const nativeTicketExpenseSheetLinkSource = readFileSync(
  path.join(repositoryRoot, ".codex", "Axapta", "INDTicketExpenseSheetLink.xpo"),
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

// Extracts an XPO method by its own ENDSOURCE marker, independently of method order.
function xpoMethod(source, methodName) {
  const startMarker = `SOURCE #${methodName}`;
  const escapedMethodName = methodName.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const startMatch = new RegExp(`SOURCE #${escapedMethodName}(?=\\r?\\n|\\r)`).exec(source);
  assert.ok(startMatch, `Missing source marker: ${startMarker}`);
  const start = startMatch.index;

  const endMarker = "ENDSOURCE";
  const end = source.indexOf(endMarker, start + startMarker.length);
  assert.notEqual(end, -1, `Missing ENDSOURCE marker for: ${startMarker}`);
  return source.slice(start, end + endMarker.length).replace(/^\s*#/gm, "");
}

const duplicateValidationSource = xpoMethod(ticketTableSource, "validateUniqueTicketDateTime");
const axTicketCreateSource = xpoMethod(expenseSheetServiceSource, "createExpenseSheetTicket");
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
const quickCreateFormReaderSource = sourceBetween(
  controllerSource,
  "private async Task<QuickCreateFormReadResult> ReadQuickCreateFormAsync(",
  "private void LogQuickCreateMultipart(",
);
const linkTicketToExpenseSheetSource = sourceBetween(
  controllerSource,
  "private static bool TryLinkTicketToExpenseSheet(",
  "// Reads a header and optional lines container from AX.",
);
const resolveLinkedTicketCurrencyFieldsSource = sourceBetween(
  controllerSource,
  "private static bool TryResolveLinkedTicketCurrencyFields(",
  "// Appends stable create positions 9-12 from an already validated monetary snapshot.",
);
const tryApplyTicketFromIACoreSource = sourceBetween(
  controllerSource,
  "private bool TryApplyTicketFromIACore(",
  "// Resolves ticket creation mode with backward-compatible default.",
);
const resolveTicketIaCurrencyAmountsSource = sourceBetween(
  controllerSource,
  "private bool TryResolveTicketIaCurrencyAmounts(",
  "// Uses the ticket transaction date as the exchange-rate date when available.",
);
const linkedTicketCurrencyFieldsSource = sourceBetween(
  controllerSource,
  "private static void AppendLinkedTicketLineCurrencyFields(",
  "// Returns only positive currency values because AX rejects empty or zero conversion data.",
);
const bulkLinkExpenseSheetTicketsSource = sourceBetween(
  controllerSource,
  "public IHttpActionResult BulkLinkExpenseSheetTickets(",
  "public IHttpActionResult UpdateExpenseSheetTicket(",
);
const bulkLinkSkipReasonSource = sourceBetween(
  controllerSource,
  "private static string GetBulkLinkSkipReason(",
  "// Detects duplicate-link business messages returned by AX.",
);
const bulkLinkTicketLogSource = sourceBetween(
  controllerSource,
  "private void LogBulkLinkTicketResult(",
  "// Returns a skip reason when a ticket is not eligible for bulk linking.",
);
const boundedLogValueSource = sourceBetween(
  controllerSource,
  "private static string ToBoundedLogValue(",
  "// Formats string length for logs so null and empty JSON payloads are distinguishable.",
);
const mapExpenseSheetTicketDetailSource = sourceBetween(
  controllerSource,
  "private static ExpenseSheetTicketDetailDto MapExpenseSheetTicketDetail(",
  "private static List<ExpenseSheetTicketListItemDto> MapExpenseSheetTicketList(",
);
const createExpenseSheetHeaderContainerSource = sourceBetween(
  expenseSheetsControllerSource,
  "var headerCon = ax.CreateContainer();",
  "rootCon.Append(headerCon);",
);
const updateExpenseSheetHeaderSource = sourceBetween(
  expenseSheetsControllerSource,
  "public IHttpActionResult UpdateExpenseSheetHeader(",
  "public IHttpActionResult PropagateExpenseSheetCurrencyDefaults(",
);
const propagateExpenseSheetProjectDefaultSource = sourceBetween(
  expenseSheetsControllerSource,
  "public IHttpActionResult PropagateExpenseSheetProjectDefault(",
  "public IHttpActionResult PropagateExpenseSheetReimbursableExpense(",
);
const appendCreateHeaderOptionalFieldsSource = sourceBetween(
  expenseSheetsControllerSource,
  "private static void AppendCreateHeaderOptionalFields(",
  "// Materializes update-header positions 8-12 before the project intent flag at position 13.",
);
const appendUpdateHeaderOptionalFieldsSource = sourceBetween(
  expenseSheetsControllerSource,
  "private static void AppendUpdateHeaderOptionalFields(",
  "// Reads optional forwarding headers without making them part of the public body contract.",
);
const updateExpenseSheetLineSource = sourceBetween(
  expenseSheetsControllerSource,
  "public IHttpActionResult UpdateExpenseSheetLine(",
  "public IHttpActionResult LinkExpenseSheetLineTicket(",
);
const appendLineOptionalFieldsSource = sourceBetween(
  expenseSheetsControllerSource,
  "private static void AppendLineOptionalFields(",
  "// Adds model binding/deserialization errors to standard validation list.",
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
const lineTicketAssociationSource = sourceBetween(
  expenseSheetsControllerSource,
  "private IHttpActionResult ChangeExpenseSheetLineTicketAssociation(",
  "private static IndApiResponse<object> BuildExpenseSheetLineTicketError(",
);
const axLinkLineTicketSource = xpoMethod(expenseSheetServiceSource, "linkExpenseSheetLineTicket");
const axUnlinkLineTicketSource = xpoMethod(expenseSheetServiceSource, "unlinkExpenseSheetLineTicket");
const axRefreshTicketStatusSource = xpoMethod(
  expenseSheetServiceSource,
  "refreshTicketStatusByFileId",
);
const axWritableHeaderReimbursementValidatorSource = xpoMethod(
  expenseSheetServiceSource,
  "isWritableReimbursableExpense",
);
const reimbursableAmountSource = xpoMethod(expenseSheetLineSource, "recalculateReimbursableAmount");
const realProjectResolverSource = xpoMethod(expenseSheetLineSource, "resolveRealProjectId");
const initLineFromSheetSource = xpoMethod(expenseSheetLineSource, "InitFromHojaGastosTable");
const initLineFromPreviousSource = xpoMethod(expenseSheetLineSource, "InitFromPreviousLine");
const insertExpenseSheetLineSource = xpoMethod(expenseSheetLineSource, "insert");
const syncLinkedTicketSource = xpoMethod(expenseSheetLineSource, "syncLinkedTicket");
const updateExpenseSheetLineTableSource = xpoMethod(expenseSheetLineSource, "update");
const deleteExpenseSheetLineSource = xpoMethod(expenseSheetLineSource, "delete");
const linkedTicketMonetarySnapshotSource = xpoMethod(
  expenseSheetServiceSource,
  "buildLinkedTicketMonetarySnapshot",
);
const axCreateExpenseSheetSource = xpoMethod(expenseSheetServiceSource, "createExpenseSheet");
const axGetExpenseSheetTicketSource = xpoMethod(
  expenseSheetServiceSource,
  "getExpenseSheetTicket",
);
const axNativeTicketExpenseSheetLinkRunSource = xpoMethod(
  nativeTicketExpenseSheetLinkSource,
  "run",
);
const axUpdateExpenseSheetHeaderSource = xpoMethod(
  expenseSheetServiceSource,
  "updateExpenseSheetHeader",
);
const axUpdateExpenseSheetLineSource = xpoMethod(expenseSheetServiceSource, "updateExpenseSheetLine");
const validateExpenseSheetLineForApiSource = xpoMethod(
  expenseSheetServiceSource,
  "validateExpenseSheetLineForApi",
);
const validateLineProjectFieldSource = xpoMethod(expenseSheetLineSource, "validateField");
const validateLineProjectWriteSource = xpoMethod(expenseSheetLineSource, "validateWrite");
const defaultLineProjectSource = xpoMethod(expenseSheetHeaderSource, "defaultProjectForNewLine");
const insertExpenseSheetHeaderSource = xpoMethod(expenseSheetHeaderSource, "insert");
const updateExpenseSheetHeaderTableSource = xpoMethod(expenseSheetHeaderSource, "update");
const validateExpenseSheetHeaderWriteSource = xpoMethod(expenseSheetHeaderSource, "validateWrite");
const recalculateHeaderProjectSource = xpoMethod(
  expenseSheetHeaderSource,
  "recalculateProjectFromLines",
);
const markHeaderProjectAggregateSource = xpoMethod(
  expenseSheetHeaderSource,
  "markHeaderVariousFromLine",
);
const propagateHeaderProjectSource = xpoMethod(
  expenseSheetServiceSource,
  "propagateExpenseSheetProjectDefault",
);
const purchParametersUpdateSource = xpoMethod(purchParametersSource, "Update");
const purchParametersValidateFieldSource = xpoMethod(purchParametersSource, "validateField");
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

// Mirrors the nullable header project-intent fallback used by legacy API callers.
function resolveHeaderProjectProvided(flag, projId) {
  return flag ?? (projId !== null);
}

// The optional intent position is emitted only when a new client sends the flag.
function hasExplicitLineProjectIntent(flag) {
  return flag !== null && flag !== undefined;
}

test("XPO method extraction is exact and independent of export order", () => {
  const reorderedSource =
    "SOURCE #updateSearchKey\r#prefix();\rENDSOURCE\r" +
    "SOURCE #otherMethod\r#other();\rENDSOURCE\r" +
    "SOURCE #update\r#target();\rENDSOURCE\r";

  const extracted = xpoMethod(reorderedSource, "update");
  assert.match(extracted, /target\(\);/);
  assert.doesNotMatch(extracted, /prefix\(\)|other\(\);/);
});

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

test("header project intent is stable at AX position 13 without changing create", () => {
  assert.match(
    updateExpenseSheetHeaderRequestSource,
    /public bool\? projIdProvided \{ get; set; \}/,
  );
  assert.match(
    updateExpenseSheetHeaderSource,
    /var projectProvided = body\.projIdProvided \?\? \(body\.projId != null\);/,
  );
  assert.match(
    updateExpenseSheetHeaderSource,
    /con\.Append\(projectProvided \? \(body\.projId\?\.Trim\(\) \?\? string\.Empty\) : string\.Empty\);/,
  );

  const optionalFieldsPosition = updateExpenseSheetHeaderSource.indexOf(
    "AppendUpdateHeaderOptionalFields(",
  );
  const projectIntentPosition = updateExpenseSheetHeaderSource.indexOf(
    "con.Append(ToAxBool(projectProvided));",
  );
  assert.ok(optionalFieldsPosition >= 0 && projectIntentPosition > optionalFieldsPosition);

  const baseContainerStart = updateExpenseSheetHeaderSource.indexOf("con.Append(company);");
  const baseHeaderAppends = updateExpenseSheetHeaderSource
    .slice(baseContainerStart, optionalFieldsPosition)
    .match(/con\.Append\(/g) ?? [];
  const stableOptionalAppends =
    appendUpdateHeaderOptionalFieldsSource.match(/container\.Append\(/g) ?? [];
  assert.equal(baseHeaderAppends.length, 7);
  assert.equal(stableOptionalAppends.length, 5);
  assert.equal(baseHeaderAppends.length + stableOptionalAppends.length + 1, 13);
  assert.doesNotMatch(
    appendUpdateHeaderOptionalFieldsSource,
    /!expenseSheetStatus\.HasValue[\s\S]*return;/,
  );

  const stablePositionMarkers = [
    "expenseSheetStatus.HasValue",
    "exchangeRateMode.HasValue",
    "estadoComentarios != null",
    "reimbursableExpense.HasValue",
    "!string.IsNullOrWhiteSpace(actorAxUserId)",
  ];
  let previousPosition = -1;
  for (const marker of stablePositionMarkers) {
    const currentPosition = appendUpdateHeaderOptionalFieldsSource.indexOf(marker);
    assert.ok(currentPosition > previousPosition, `Unstable AX field order at ${marker}`);
    previousPosition = currentPosition;
  }

  assert.equal(resolveHeaderProjectProvided(null, null), false);
  assert.equal(resolveHeaderProjectProvided(null, ""), true);
  assert.equal(resolveHeaderProjectProvided(null, "LEGACY-PROJECT"), true);
  assert.equal(resolveHeaderProjectProvided(false, "STALE-PROJECT"), false);
  assert.equal(resolveHeaderProjectProvided(true, null), true);

  assert.match(createExpenseSheetHeaderContainerSource, /AppendCreateHeaderOptionalFields\(/);
  assert.doesNotMatch(
    createExpenseSheetHeaderContainerSource,
    /projIdProvided|projectProvided|ToAxBool/,
  );
  assert.doesNotMatch(
    appendCreateHeaderOptionalFieldsSource,
    /projIdProvided|projectProvided|ToAxBool/,
  );

  const createTool = mcpToolsDocument.tools.find(
    ({ name }) => name === "crm_expensesheets_create",
  );
  const updateTool = mcpToolsDocument.tools.find(
    ({ name }) => name === "crm_expensesheets_update_header",
  );
  assert.equal(
    createTool.inputSchema.properties.body.properties.projIdProvided,
    undefined,
  );
  assert.equal(
    createTool.inputSchema.properties.body.properties.lines.items.properties.projIdProvided.type,
    "boolean",
  );
  assert.equal(
    updateTool.inputSchema.properties.body.properties.projIdProvided.type,
    "boolean",
  );
});

test("line update project intent is stable at AX position 17", () => {
  assert.match(
    updateExpenseSheetLineRequestSource,
    /public bool\? projIdProvided \{ get; set; \}/,
  );
  assert.match(
    updateExpenseSheetLineSource,
    /var projectIdForAx = body\.projIdProvided == false\s*\? string\.Empty\s*: body\.projId\?\.Trim\(\) \?\? string\.Empty;[\s\S]*con\.Append\(projectIdForAx\);/,
  );
  assert.match(
    updateExpenseSheetLineSource,
    /if \(body\.projIdProvided\.HasValue\)\s*con\.Append\(ToAxBool\(body\.projIdProvided\.Value\)\);/,
  );
  assert.match(
    updateExpenseSheetLineSource,
    /AppendLineOptionalFields\([\s\S]*body\.exchRate,\s*forceStablePositions: true\);/,
  );

  const optionalFieldsPosition = updateExpenseSheetLineSource.indexOf(
    "AppendLineOptionalFields(",
  );
  const projectIntentPosition = updateExpenseSheetLineSource.indexOf(
    "if (body.projIdProvided.HasValue)",
  );
  assert.ok(optionalFieldsPosition >= 0 && projectIntentPosition > optionalFieldsPosition);

  const baseContainerStart = updateExpenseSheetLineSource.indexOf("con.Append(company);");
  const baseLineAppends = updateExpenseSheetLineSource
    .slice(baseContainerStart, optionalFieldsPosition)
    .match(/con\.Append\(/g) ?? [];
  assert.equal(baseLineAppends.length, 12);
  assert.equal(baseLineAppends.length + 4 + 1, 17);

  const stableFieldsSource = appendLineOptionalFieldsSource.slice(
    appendLineOptionalFieldsSource.indexOf('const string noOptionalValueToken = "null";'),
  );
  const stablePositionMarkers = [
    "reimbursableExpense.HasValue",
    "hasCurrencyCode ? currencyCode.Trim() : string.Empty",
    "amountMST.HasValue",
    "exchRate.HasValue",
  ];
  let previousPosition = -1;
  for (const marker of stablePositionMarkers) {
    const currentPosition = stableFieldsSource.indexOf(marker);
    assert.ok(currentPosition > previousPosition, `Unstable AX line field order at ${marker}`);
    previousPosition = currentPosition;
  }

  assert.equal(hasExplicitLineProjectIntent(null), false);
  assert.equal(hasExplicitLineProjectIntent(undefined), false);
  assert.equal(hasExplicitLineProjectIntent(false), true);
  assert.equal(hasExplicitLineProjectIntent(true), true);

  const propagateProjectTool = mcpToolsDocument.tools.find(
    ({ name }) => name === "crm_expensesheets_propagate_project_default",
  );
  assert.equal(
    propagateProjectTool.inputSchema.properties.body.properties.projIdProvided.type,
    "boolean",
  );
  assert.equal(propagateProjectTool.inputSchema.required.includes("body"), false);

  const updateLineTool = mcpToolsDocument.tools.find(
    ({ name }) => name === "crm_expensesheets_update_line",
  );
  assert.equal(
    updateLineTool.inputSchema.properties.body.properties.projIdProvided.type,
    "boolean",
  );
  assert.match(
    axUpdateExpenseSheetLineSource,
    /legacyProjectContract\s*=\s*conLen\(_data\) < 17;/,
  );
  assert.match(
    axUpdateExpenseSheetLineSource,
    /projectProvided\s*=\s*legacyProjectContract \|\| any2int\(conPeek\(_data, 17\)\) != 0;/,
  );
  assert.match(
    axUpdateExpenseSheetLineSource,
    /if \(legacyProjectContract && !projId\)[\s\S]*projId\s*=\s*header\.defaultProjectForNewLine\(\);[\s\S]*if \(!projId\)[\s\S]*projectProvided\s*=\s*false;/,
  );
  assert.match(
    axUpdateExpenseSheetLineSource,
    /if \(projectProvided && projId != line\.ProjIdHornos && projId/,
  );
  assert.match(
    axUpdateExpenseSheetLineSource,
    /if \(projectProvided\)[\s\S]*line\.ProjId\s*= projId;[\s\S]*line\.ProjIdHornos = projId;/,
  );
});

test("project intent endpoints reject malformed nullable booleans", () => {
  assert.match(
    updateExpenseSheetLineSource,
    /if \(!ModelState\.IsValid\)\s*AddModelStateErrors\(validationErrors\);/,
  );
  assert.match(
    propagateExpenseSheetProjectDefaultSource,
    /if \(!ModelState\.IsValid\)\s*AddModelStateErrors\(validationErrors\);/,
  );
});

test("normal header updates do not propagate project changes to existing lines", () => {
  assert.match(
    axUpdateExpenseSheetHeaderSource,
    /if \(projectProvided\)\s*header\.ProjId\s*=\s*projId;/,
  );
  assert.doesNotMatch(axUpdateExpenseSheetHeaderSource, /updateProjectDefaultInLines\(/);
  assert.match(
    axUpdateExpenseSheetHeaderSource,
    /if \(projectChanged && projId &&[\s\S]*!CRMHojaGastosLine::resolveEligibleProjectId\(projId\)\)/,
  );
  assert.match(
    propagateHeaderProjectSource,
    /updated\s*=\s*header\.updateProjectDefaultInLines\(projectId\);/,
  );
});

test("unchanged historical line projects remain editable after project closure", () => {
  const strictEligibilityPredicate =
    /if \(this\.ProjIdHornos\s*&&\s*\(!this\.RecId \|\| this\.ProjIdHornos != this\.orig\(\)\.ProjIdHornos\)\s*&&\s*!CRMHojaGastosLine::resolveEligibleProjectId\(this\.ProjIdHornos\)\)/;
  const imputableProjectPredicate =
    /else if \(this\.ProjIdHornos\s*&&\s*\(!this\.RecId \|\| this\.ProjIdHornos != this\.orig\(\)\.ProjIdHornos\)\s*&&\s*!ProjTable::find\(this\.ProjIdHornos\)\.INDPermitirImputarGastos\)/;

  assert.match(validateLineProjectFieldSource, strictEligibilityPredicate);
  assert.match(validateLineProjectWriteSource, strictEligibilityPredicate);
  assert.match(validateLineProjectWriteSource, imputableProjectPredicate);
  assert.match(
    validateExpenseSheetLineForApiSource,
    /validateField\(FieldNum\(CRMHojaGastosLine, ProjIdHornos\)\)/,
  );
  assert.match(
    validateLineProjectFieldSource,
    /if \(!this\.RecId \|\| this\.ProjIdHornos != this\.orig\(\)\.ProjIdHornos\)\s*\{[\s\S]*ProjTable::PermitirCambioProyecto\(projTableOrig\.ProjId,\s*projTable\.ProjId\);[\s\S]*\}/,
  );
});

test("programmatic line and header writes enforce eligible project changes", () => {
  assert.match(
    insertExpenseSheetLineSource,
    /if \(this\.ProjIdHornos &&[\s\S]*!CRMHojaGastosLine::resolveEligibleProjectId\(this\.ProjIdHornos\)\)[\s\S]*throw error\(/,
  );
  assert.match(
    updateExpenseSheetLineTableSource,
    /if \(this\.ProjIdHornos != this\.orig\(\)\.ProjIdHornos &&[\s\S]*this\.ProjIdHornos &&[\s\S]*!CRMHojaGastosLine::resolveEligibleProjectId\(this\.ProjIdHornos\)\)[\s\S]*throw error\(/,
  );
  assert.match(
    validateLineProjectWriteSource,
    /this\.ProjIdHornos &&[\s\S]*\(!this\.RecId \|\| this\.ProjIdHornos != this\.orig\(\)\.ProjIdHornos\)[\s\S]*!CRMHojaGastosLine::resolveEligibleProjectId\(this\.ProjIdHornos\)/,
  );

  assert.match(
    insertExpenseSheetHeaderSource,
    /if \(this\.ProjId &&[\s\S]*!CRMHojaGastosLine::resolveEligibleProjectId\(this\.ProjId\)\)[\s\S]*throw error\(/,
  );
  assert.match(
    updateExpenseSheetHeaderTableSource,
    /if \(this\.ProjId != tableOrig\.ProjId &&[\s\S]*this\.ProjId &&[\s\S]*!CRMHojaGastosLine::resolveEligibleProjectId\(this\.ProjId\)\)[\s\S]*throw error\(/,
  );
  assert.match(
    validateExpenseSheetHeaderWriteSource,
    /projectChanged\s*=\s*this\.ProjId != this\.orig\(\)\.ProjId;[\s\S]*if \(projectChanged && this\.ProjId\)[\s\S]*eligibleProjectId\s*=\s*CRMHojaGastosLine::resolveEligibleProjectId\(this\.ProjId\);[\s\S]*else if \(projectChanged && this\.ProjId && !eligibleProjectId\)[\s\S]*checkFailed\(/,
  );
  assert.match(
    validateExpenseSheetHeaderWriteSource,
    /if \(projectChanged\)\s*\{[\s\S]*ProjTable::PermitirCambioProyecto\(projTableOrig\.ProjId,\s*projTable\.ProjId\);[\s\S]*\}/,
  );
  assert.equal(
    validateExpenseSheetHeaderWriteSource.match(/ProjTable::PermitirCambioProyecto\(/g)?.length,
    1,
  );
  assert.match(
    markHeaderProjectAggregateSource,
    /if \(mustUpdate\)[\s\S]*this\.doUpdate\(\);/,
  );
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
  assert.doesNotMatch(
    expenseSheetServiceSource,
    /hasReimbursableExpense\s*&&[\s\S]*isWritableReimbursableExpense\(any2int\(header\.ReimbursableExpense\)\)/,
  );
  assert.match(
    expenseSheetServiceSource,
    /if \(hasReimbursableExpense\)[\s\S]*header\.ReimbursableExpense = reimbursableExpense;/,
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

test("line-ticket association accepts signed non-zero line RecIds", () => {
  assert.match(
    lineTicketAssociationSource,
    /AddLineRecIdValidation\(validationErrors,\s*lineRecId\);/,
  );
  assert.doesNotMatch(lineTicketAssociationSource, /lineRecId\s*<=\s*0/);
  assert.doesNotMatch(lineTicketAssociationSource, /lineRecId debe ser mayor que cero/);
  assert.doesNotMatch(
    expenseSheetsControllerSource,
    /Positive persisted expense sheet line identifier/,
  );
});

test("line-ticket association is owner-only and requires sheet Edit plus ticket View", () => {
  assert.match(
    lineTicketAssociationSource,
    /string\.Equals\(ownerAxUserId\.Trim\(\),\s*viewerAxUserId\.Trim\(\),\s*StringComparison\.OrdinalIgnoreCase\)/,
  );

  for (const source of [axLinkLineTicketSource, axUnlinkLineTicketSource]) {
    assert.match(
      source,
      /strUpr\(strLRTrim\(viewerAxUserId\)\)\s*!=\s*strUpr\(strLRTrim\(ownerAxUserId\)\)/,
    );
    assert.match(source, /'GASTOS_HOJA_GASTO'[\s\S]*AccessRights\s*<\s*SysAccessRights::Edit/);
    assert.match(source, /'GASTOS_TICKETS'[\s\S]*AccessRights\s*<\s*SysAccessRights::View/);
  }
});

test("link replaces only the manual line monetary snapshot from the ticket", () => {
  assert.match(
    axLinkLineTicketSource,
    /line\.FileId\s*=\s*fileId;[\s\S]*line\.Qty\s*=\s*1;[\s\S]*line\.Price\s*=\s*ticketHeader\.TotalAmount;[\s\S]*line\.Amount\s*=\s*ticketHeader\.TotalAmount;[\s\S]*line\.Currency\s*=\s*ticketHeader\.CurrencyCode;[\s\S]*line\.ExchRate\s*=\s*ticketHeader\.ExchRate;[\s\S]*line\.AmountMST\s*=\s*ticketHeader\.AmountMST;/,
  );
  assert.match(
    axLinkLineTicketSource,
    /line\.normalizeCurrencyAmounts\(line\.AmountMST\s*!=\s*0\);[\s\S]*line\.recalculateReimbursableAmount\(\);[\s\S]*line\.validateCurrencyAmounts\(\)[\s\S]*line\.doUpdate\(\);[\s\S]*line\.CreaActualizaProyectoEnLineCust\(\);[\s\S]*INDProjCostRevenueTable::CreateProjCostFromCommon\(line\);[\s\S]*line\.syncHojaGastosTable\(\);[\s\S]*refreshTicketStatusByFileId\(ownerAxUserId,\s*fileId\)/,
  );
  assert.match(axLinkLineTicketSource, /ticketHeader\.TotalAmount\s*<=\s*0/);
  assert.doesNotMatch(axLinkLineTicketSource, /line\.update\(\);/);
  assert.doesNotMatch(axLinkLineTicketSource, /line\.syncLinkedTicket\(\);/);

  for (const preservedField of [
    "TransDate",
    "Type",
    "Description",
    "ProjId",
    "ProjIdHornos",
    "Internacional",
    "ReimbursableExpense",
  ]) {
    assert.doesNotMatch(
      axLinkLineTicketSource,
      new RegExp(`line\\.${preservedField}\\s*=(?!=)`),
      `${preservedField} must remain unchanged when a ticket is linked`,
    );
  }
});

test("relinking the same FileId reconciles amounts before returning", () => {
  const sameFileComparisonPosition = axLinkLineTicketSource.indexOf(
    "line.FileId == fileId",
  );
  const monetarySnapshotPosition = axLinkLineTicketSource.indexOf("line.Qty = 1;");

  assert.notEqual(sameFileComparisonPosition, -1, "Missing same-FileId detection");
  assert.notEqual(monetarySnapshotPosition, -1, "Missing ticket monetary snapshot");
  assert.ok(
    monetarySnapshotPosition > sameFileComparisonPosition,
    "Monetary reconciliation must run after detecting the same FileId",
  );
  assert.doesNotMatch(
    axLinkLineTicketSource.slice(sameFileComparisonPosition, monetarySnapshotPosition),
    /ttscommit;|buildExpenseSheetLineTicketResult\(true,/,
    "The same-FileId path must not commit or return before monetary reconciliation",
  );
});

test("ticket status refresh cannot synchronize ticket fields back into the line", () => {
  assert.match(
    axRefreshTicketStatusSource,
    /ticketHeader\.updateSearchKey\(\);[\s\S]*ticketHeader\.doUpdate\(\);/,
  );
  assert.doesNotMatch(axRefreshTicketStatusSource, /ticketHeader\.update\(\);/);
  assert.doesNotMatch(axRefreshTicketStatusSource, /syncHojaGastoLine\(\);/);
});

test("unlink clears FileId before deriving ticket status", () => {
  assert.match(
    axUnlinkLineTicketSource,
    /previousFileId\s*=\s*line\.FileId;[\s\S]*line\.FileId\s*=\s*'';[\s\S]*line\.update\(\);[\s\S]*refreshTicketStatusByFileId\(ownerAxUserId,\s*previousFileId\)/,
  );
  assert.match(
    axRefreshTicketStatusSource,
    /!hasAssignedLine\s*&&\s*ticketHeader\.Status\s*!=\s*INDTicketStatus::Pending[\s\S]*ticketHeader\.Status\s*=\s*INDTicketStatus::Pending/,
  );
  assert.match(
    axRefreshTicketStatusSource,
    /hasAssignedLine\s*&&\s*ticketHeader\.Status\s*!=\s*INDTicketStatus::Assigned[\s\S]*ticketHeader\.Status\s*=\s*INDTicketStatus::Assigned/,
  );
});

test("expense lines inherit only the eligible project stored in the header", () => {
  assert.match(
    realProjectResolverSource,
    /_projId\s*==\s*purchParameters\.INDProjIdVarious/,
  );
  assert.match(realProjectResolverSource, /projTable\s*=\s*ProjTable::find\(_projId\);/);
  assert.match(
    realProjectResolverSource,
    /return\s+projTable\.RecId\s*\?\s*projTable\.ProjId\s*:\s*'';/,
  );
  assert.match(
    initLineFromSheetSource,
    /this\.ProjIdHornos\s*=\s*hojaGastosTable\.defaultProjectForNewLine\(\);/,
  );
  assert.match(
    initLineFromPreviousSource,
    /this\.ProjIdHornos\s*=\s*this\.HojaGastosTable\(\)\.defaultProjectForNewLine\(\);/,
  );
  assert.doesNotMatch(
    initLineFromPreviousSource,
    /resolveRealProjectId\(_hojaGastosLine\.ProjIdHornos\)/,
  );
  assert.match(
    axCreateExpenseSheetSource,
    /resolvedLineProjId\s*=\s*CRMHojaGastosLine::resolveRealProjectId\(lineProjId\);/,
  );
  assert.match(
    axCreateExpenseSheetSource,
    /conLen\(lineIn\)\s*>=\s*13[\s\S]*conPeek\(lineIn,\s*13\)/,
  );
  assert.match(
    axCreateExpenseSheetSource,
    /resolvedLineProjId\s*=\s*header\.defaultProjectForNewLine\(\);/,
  );
  assert.match(axCreateExpenseSheetSource, /line\.ProjId\s*=\s*resolvedLineProjId;/);
  assert.match(axCreateExpenseSheetSource, /line\.ProjIdHornos\s*=\s*resolvedLineProjId;/);
  assert.doesNotMatch(
    axCreateExpenseSheetSource,
    /line\.ProjId(?:Hornos)?\s*=\s*(?:lineProjId|header\.ProjId);/,
  );
  assert.match(
    defaultLineProjectSource,
    /return\s+CRMHojaGastosLine::resolveEligibleProjectId\(this\.ProjId\);/,
  );
  assert.doesNotMatch(defaultLineProjectSource, /lastLine|select\s+(?:firstonly\s+)?CRMHojaGastosLine/i);
  assert.match(
    recalculateHeaderProjectSource,
    /if \(!hasLines\)[\s\S]*calculatedProjectId\s*=\s*'';/,
  );
  assert.match(
    recalculateHeaderProjectSource,
    /if \(!hasLines\)[\s\S]*commonProjectId\s*=\s*lineProjectId;[\s\S]*hasLines\s*=\s*true;[\s\S]*else if \(lineProjectId\s*!=\s*commonProjectId\)/,
  );
  assert.match(recalculateHeaderProjectSource, /lineProjectId\s*!=\s*commonProjectId/);
  assert.match(
    recalculateHeaderProjectSource,
    /calculatedProjectId\s*=\s*purchParameters\.INDProjIdVarious/,
  );
  assert.match(
    recalculateHeaderProjectSource,
    /else\s*\{\s*calculatedProjectId\s*=\s*commonProjectId;/,
  );
  assert.match(
    recalculateHeaderProjectSource,
    /if \(!purchParameters\.INDProjIdVarious\)[\s\S]*throw error\(/,
  );
  assert.match(
    propagateHeaderProjectSource,
    /conLen\(_data\)\s*>=\s*5[\s\S]*conPeek\(_data,\s*5\)/,
  );
});

test("deleting an expense line recalculates both header aggregates exactly once", () => {
  assert.match(deleteExpenseSheetLineSource, /if \(hojaGastosTable\)/);
  assert.doesNotMatch(
    deleteExpenseSheetLineSource,
    /if \(hojaGastosTable\s*&&\s*hojaGastosTable\.recalculateReimbursableExpenseFromLines\(\)\)/,
  );
  assert.match(
    deleteExpenseSheetLineSource,
    /mustUpdate\s*=\s*hojaGastosTable\.recalculateProjectFromLines\(\);/,
  );
  assert.equal(
    deleteExpenseSheetLineSource.match(/recalculateReimbursableExpenseFromLines\(\)/g)?.length,
    1,
  );
  assert.match(
    deleteExpenseSheetLineSource,
    /if \(mustUpdate\)[\s\S]*hojaGastosTable\.doUpdate\(\);/,
  );
});

test("PurchParameters enforces and migrates the reserved mixed-project marker", () => {
  assert.match(
    purchParametersSource,
    /FIELD #INDProjIdVarious[\s\S]*?Mandatory\s+#Yes[\s\S]*?ENDPROPERTIES/,
  );
  assert.match(
    purchParametersUpdateSource,
    /rawMarker\s*=\s*this\.INDProjIdVarious;[\s\S]*markerChanged\s*=\s*rawMarker\s*!=\s*oldMarker;[\s\S]*newMarker\s*=\s*strLRTrim\(rawMarker\);/,
  );
  assert.match(
    purchParametersUpdateSource,
    /if \(markerChanged\)[\s\S]*this\.INDProjIdVarious\s*=\s*newMarker;[\s\S]*if \(!newMarker\)[\s\S]*throw error\([\s\S]*projTable\s*=\s*ProjTable::find\(newMarker\);[\s\S]*if \(projTable\.RecId\)[\s\S]*throw error\(/,
  );
  assert.match(
    purchParametersUpdateSource,
    /migrateMarker\s*=\s*markerChanged\s*&&[\s\S]*oldMarker\s*&&[\s\S]*oldMarker\s*!=\s*newMarker;/,
  );
  assert.match(
    purchParametersUpdateSource,
    /ttsbegin;[\s\S]*CRMHojaGastosTable::migrateVariousProjectMarker\(\s*oldMarker,\s*newMarker\s*\);[\s\S]*super\(\);[\s\S]*ttscommit;/,
  );
  assert.match(
    purchParametersValidateFieldSource,
    /_fieldIdToCheck\s*==\s*fieldNum\(PurchParameters,\s*INDProjIdVarious\)[\s\S]*marker\s*=\s*strLRTrim\(this\.INDProjIdVarious\);/,
  );
  assert.equal(
    purchParametersValidateFieldSource.match(/checkFailed\(/g)?.length,
    2,
  );
});

test("automatic ticket links preserve the create-line project tri-state", () => {
  assert.match(
    quickCreateFormReaderSource,
    /var standardProjectId = await ReadFormFieldAsync\(provider, "projId"\)/,
  );
  assert.match(
    quickCreateFormReaderSource,
    /var projectProvided = standardProjectId != null \|\| legacyProjectId != null;/,
  );
  assert.match(quickCreateFormReaderSource, /ProjectProvided = projectProvided/);
  assert.match(
    quickCreateSource,
    /quickCreateForm\.ProjectId,\s*quickCreateForm\.ProjectProvided,\s*out var linkMessage/,
  );
  assert.match(
    controllerSource,
    /targetInfo\.ProjId,\s*false,\s*out var linkMessage/,
  );
  assert.match(
    linkTicketToExpenseSheetSource,
    /string projectId,\s*bool projectProvided,\s*out string message/,
  );
  assert.match(
    linkTicketToExpenseSheetSource,
    /lineCon\.Append\(projectProvided \? \(projectId \?\? string\.Empty\)\.Trim\(\) : string\.Empty\);/,
  );

  const currencyFieldsPosition = linkTicketToExpenseSheetSource.indexOf(
    "AppendLinkedTicketLineCurrencyFields(lineCon, currencyCode, amountMST, exchRate);",
  );
  const projectFlagPosition = linkTicketToExpenseSheetSource.indexOf(
    "lineCon.Append(ToAxBool(projectProvided));",
  );
  assert.ok(currencyFieldsPosition >= 0 && projectFlagPosition > currencyFieldsPosition);

  const stableFieldAppends = linkedTicketCurrencyFieldsSource.match(/lineCon\.Append\(/g) ?? [];
  assert.equal(stableFieldAppends.length, 4);
});

test("bulk strict linking propagates a valid BRL monetary snapshot", () => {
  assert.match(
    resolveLinkedTicketCurrencyFieldsSource,
    /currencyCode\s*=\s*\(ticketDetail\.CurrencyCode \?\? string\.Empty\)\.Trim\(\)\.ToUpperInvariant\(\);/,
  );
  assert.match(
    resolveLinkedTicketCurrencyFieldsSource,
    /amountMST\s*=\s*NormalizePositiveCurrencyValue\(ticketDetail\.TotalAmountMST \?\? ticketDetail\.AmountMST\);/,
  );
  assert.match(
    resolveLinkedTicketCurrencyFieldsSource,
    /exchRate\s*=\s*NormalizePositiveCurrencyValue\(ticketDetail\.ExchRate\);/,
  );
  const completeSnapshotPosition = resolveLinkedTicketCurrencyFieldsSource.indexOf(
    "if (amountMST.HasValue && exchRate.HasValue)",
  );
  const missingSnapshotPosition = resolveLinkedTicketCurrencyFieldsSource.indexOf(
    "var missingFields =",
    completeSnapshotPosition,
  );
  assert.ok(
    completeSnapshotPosition >= 0 && missingSnapshotPosition > completeSnapshotPosition,
    "A complete foreign snapshot must succeed before missing fields are rejected",
  );
  assert.match(
    linkTicketToExpenseSheetSource,
    /AppendLinkedTicketLineCurrencyFields\(lineCon, currencyCode, amountMST, exchRate\);/,
  );
  assert.match(
    linkedTicketCurrencyFieldsSource,
    /lineCon\.Append\(noOptionalValueToken\);[\s\S]*lineCon\.Append\([^;]*currencyCode[^;]*\);[\s\S]*lineCon\.Append\([^;]*amountMST[^;]*\);[\s\S]*lineCon\.Append\([^;]*exchRate[^;]*\);/,
  );
});

test("ticket linking never synthesizes missing monetary values", () => {
  assert.doesNotMatch(
    resolveLinkedTicketCurrencyFieldsSource,
    /QuickCreateInsertFallbackAmount|LinkedTicketCurrencyPolicy|currencyPolicy/,
    "Persisted ticket linking must not retain a synthetic monetary fallback",
  );
  assert.match(
    resolveLinkedTicketCurrencyFieldsSource,
    /if \(amountMST\.HasValue && exchRate\.HasValue\)\s*return true;[\s\S]{0,500}message\s*=[\s\S]{0,500}return false;/,
  );
});

test("EUR links keep the local contract without optional foreign values", () => {
  assert.match(
    resolveLinkedTicketCurrencyFieldsSource,
    /amountMST\s*=\s*null;[\s\S]*exchRate\s*=\s*null;/,
  );
  assert.match(
    resolveLinkedTicketCurrencyFieldsSource,
    /if \(string\.Equals\(currencyCode, ExpenseSheetLocalCurrencyCode, StringComparison\.OrdinalIgnoreCase\)\)\s*return true;/,
  );
  assert.match(
    linkedTicketCurrencyFieldsSource,
    /lineCon\.Append\([^;]*(?:includeCurrencyValues|isForeignCurrency)[^;]*\?\s*currencyCode\s*:\s*string\.Empty\);/,
  );
  assert.match(
    linkedTicketCurrencyFieldsSource,
    /lineCon\.Append\(amountMST\.HasValue\s*\?\s*\(object\)amountMST\.Value\s*:\s*noOptionalValueToken\);/,
  );
  assert.match(
    linkedTicketCurrencyFieldsSource,
    /lineCon\.Append\(exchRate\.HasValue\s*\?\s*\(object\)exchRate\.Value\s*:\s*noOptionalValueToken\);/,
  );
});

test("strict BRL without AmountMST fails before the AX create call", () => {
  assert.match(
    resolveLinkedTicketCurrencyFieldsSource,
    /amountMST\s*=\s*NormalizePositiveCurrencyValue\(ticketDetail\.TotalAmountMST \?\? ticketDetail\.AmountMST\);/,
  );
  assert.match(
    resolveLinkedTicketCurrencyFieldsSource,
    /var missingFields\s*=\s*!amountMST\.HasValue[\s\S]{0,500}message\s*=[\s\S]{0,500}return false;/,
  );
  assert.ok(
    linkTicketToExpenseSheetSource.indexOf("TryResolveLinkedTicketCurrencyFields(") <
      linkTicketToExpenseSheetSource.indexOf("ax.CreateContainer()"),
    "Strict validation must run before the first AX container is created",
  );
});

test("strict BRL without ExchRate fails before the AX create call", () => {
  assert.match(
    resolveLinkedTicketCurrencyFieldsSource,
    /exchRate\s*=\s*NormalizePositiveCurrencyValue\(ticketDetail\.ExchRate\);/,
  );
  assert.match(
    resolveLinkedTicketCurrencyFieldsSource,
    /var missingFields\s*=\s*[\s\S]{0,500}!exchRate\.HasValue[\s\S]{0,500}message\s*=[\s\S]{0,500}return false;/,
  );
});

test("ticket IA keeps the strict positive amountMST gate", () => {
  assert.match(
    resolveTicketIaCurrencyAmountsSource,
    /amountMST\s*=\s*Math\.Round\(totalAmount \* providerResult\.Rate,[\s\S]{0,200}if \(!amountMST\.HasValue \|\| amountMST\.Value <= 0m\)/,
  );
  const positiveGatePosition = resolveTicketIaCurrencyAmountsSource.indexOf(
    "if (!amountMST.HasValue || amountMST.Value <= 0m)",
  );
  const validationFieldPosition = resolveTicketIaCurrencyAmountsSource.indexOf(
    'Field = "amountMST"',
    positiveGatePosition,
  );
  const rejectionPosition = resolveTicketIaCurrencyAmountsSource.indexOf(
    "return false;",
    validationFieldPosition,
  );
  assert.ok(
    positiveGatePosition >= 0 &&
      validationFieldPosition > positiveGatePosition &&
      rejectionPosition > validationFieldPosition,
    "Non-positive calculated amountMST must still be rejected before success",
  );
});

test("empty currency and non-positive original totals are failed before AX", () => {
  assert.doesNotMatch(
    bulkLinkSkipReasonSource,
    /TotalAmountCurrency|CurrencyCode/,
    "Monetary snapshot defects must reach failed[] instead of skipped[]",
  );

  assert.match(
    resolveLinkedTicketCurrencyFieldsSource,
    /currencyCode\s*=\s*\(ticketDetail\.CurrencyCode \?\? string\.Empty\)\.Trim\(\)\.ToUpperInvariant\(\);/,
  );
  assert.match(
    resolveLinkedTicketCurrencyFieldsSource,
    /string\.IsNullOrWhiteSpace\(currencyCode\)[\s\S]{0,500}message\s*=[\s\S]{0,500}return false;/,
  );
  assert.match(
    resolveLinkedTicketCurrencyFieldsSource,
    /!originalTotalAmount\.HasValue\s*\|\|\s*originalTotalAmount\.Value\s*<=\s*0m[\s\S]{0,500}return false;/,
  );
});

test("mixed bulk results keep valid links and report invalid snapshots as failed", () => {
  assert.match(
    bulkLinkExpenseSheetTicketsSource,
    /if \(!TryLinkTicketToExpenseSheet\([\s\S]*ticketReason\s*=\s*NormalizeIssueReason\(linkMessage,[\s\S]*result\.failed\.Add\([\s\S]*ticketId\s*=\s*ticketId,[\s\S]*reason\s*=\s*ticketReason[\s\S]*continue;/,
  );
  assert.match(
    bulkLinkExpenseSheetTicketsSource,
    /result\.linkedTicketIds\.Add\(ticketId\);/,
  );
  assert.match(
    bulkLinkExpenseSheetTicketsSource,
    /result\.linkedCount\s*=\s*result\.linkedTicketIds\.Count;[\s\S]*result\.skippedCount\s*=\s*result\.skipped\.Count;[\s\S]*result\.failedCount\s*=\s*result\.failed\.Count;/,
  );
  assert.match(
    bulkLinkExpenseSheetTicketsSource,
    /return Ok\(new IndApiResponse<ExpenseSheetTicketBulkLinkResultDto>[\s\S]*Success\s*=\s*true[\s\S]*Data\s*=\s*result/,
  );
});

test("quick-create settles and reloads the persisted ticket before strict linking", () => {
  const finalizePosition = quickCreateSource.indexOf("TryApplyTicketFromIACore(");
  const reloadPosition = quickCreateSource.indexOf("TryGetExpenseSheetTicketDetail(", finalizePosition);
  const linkPosition = quickCreateSource.indexOf("TryLinkTicketToExpenseSheet(", reloadPosition);
  assert.ok(finalizePosition >= 0 && reloadPosition > finalizePosition && linkPosition > reloadPosition);
  assert.match(
    tryApplyTicketFromIACoreSource,
    /TryResolveTicketIaCurrencyAmounts\([\s\S]{0,1000}resolvedAmountMST[\s\S]{0,1000}resolvedExchRate/,
  );
  assert.doesNotMatch(quickCreateSource, /LinkedTicketCurrencyPolicy|QuickCreateLegacy/);
});

test("quick-create linking rejects an incomplete persisted snapshot instead of bypassing AX defense", () => {
  assert.match(
    resolveLinkedTicketCurrencyFieldsSource,
    /if \(string\.IsNullOrWhiteSpace\(currencyCode\)\)[\s\S]{0,300}message\s*=\s*"Ticket currencyCode is empty\.";[\s\S]{0,100}return false;/,
  );
  assert.doesNotMatch(resolveLinkedTicketCurrencyFieldsSource, /QuickCreateInsertFallbackAmount/);
});

test("bulk linking emits one bounded structured outcome per requested ticket", () => {
  assert.equal(
    bulkLinkExpenseSheetTicketsSource.match(/LogBulkLinkTicketResult\(/g)?.length,
    1,
  );
  assert.match(
    bulkLinkExpenseSheetTicketsSource,
    /foreach \(var ticketId in requestedTicketIds\)[\s\S]*try[\s\S]*finally[\s\S]*LogBulkLinkTicketResult\(/,
  );
  assert.match(bulkLinkExpenseSheetTicketsSource, /ticketResult\s*=\s*"linked";/);
  assert.match(bulkLinkExpenseSheetTicketsSource, /ticketResult\s*=\s*"skipped";/);
  assert.match(bulkLinkExpenseSheetTicketsSource, /ticketResult\s*=\s*"failed";/);
  assert.match(
    bulkLinkExpenseSheetTicketsSource,
    /if \(!string\.IsNullOrWhiteSpace\(terminalSheetReason\)\)[\s\S]*ticketReason\s*=\s*terminalSheetReason;[\s\S]*result\.failed\.Add\([\s\S]*continue;/,
  );
  assert.match(
    bulkLinkExpenseSheetTicketsSource,
    /if \(!TryGetExpenseSheetTicketDetail\([\s\S]*ticketReason\s*=\s*NormalizeIssueReason\([\s\S]*result\.failed\.Add\([\s\S]*continue;/,
  );
  assert.match(
    bulkLinkExpenseSheetTicketsSource,
    /if \(IsTicketAlreadyLinkedMessage\(linkMessage\)\)[\s\S]*ticketResult\s*=\s*"skipped";[\s\S]*result\.skipped\.Add\([\s\S]*continue;/,
  );
  assert.equal(bulkLinkTicketLogSource.match(/Logger\.Log\(/g)?.length, 1);
  assert.match(
    bulkLinkTicketLogSource,
    /traceId=\{ToBoundedLogValue\(traceId\)\}[\s\S]*sheetId=\{ToBoundedLogValue\(expenseSheetId\)\}[\s\S]*ticketId=\{ToBoundedLogValue\(ticketId\)\}[\s\S]*currency=\{ToBoundedLogValue\(currencyCode\)\}[\s\S]*result=\{ToBoundedLogValue\(result\)\}[\s\S]*reason=\{ToBoundedLogValue\(reason\)\}/,
  );
  for (const field of [
    "traceId",
    "expenseSheetId",
    "ticketId",
    "currencyCode",
    "result",
    "reason",
  ]) {
    assert.match(
      bulkLinkTicketLogSource,
      new RegExp(`ToBoundedLogValue\\(${field}\\)`),
    );
  }
  assert.match(boundedLogValueSource, /var normalized\s*=\s*ToLogValue\(value\)/);
  assert.match(boundedLogValueSource, /\.Replace\('\\r', ' '\)/);
  assert.match(boundedLogValueSource, /\.Replace\('\\n', ' '\)/);
  assert.match(boundedLogValueSource, /const int maxLength\s*=\s*240;/);
  assert.match(boundedLogValueSource, /normalized\.Substring\(0, maxLength\)/);
  assert.doesNotMatch(
    bulkLinkTicketLogSource,
    /description|ocr|fileName|user|axUserId/i,
  );
});

test("AX ticket detail monetary fields map to the intended DTO aliases", () => {
  assert.match(
    axGetExpenseSheetTicketSource,
    /Currency::find\(ticketHeader\.CurrencyCode\)\.CurrencyCodeISO[\s\S]*ticketHeader\.TotalAmount[\s\S]*ticketHeader\.AmountMST[\s\S]*ticketHeader\.ExchRate/,
  );
  assert.match(
    mapExpenseSheetTicketDetailSource,
    /var totalAmountCurrency\s*=\s*headerExtras\.Count > 4 \? ToDecimal\(headerExtras\[4\]\) : null;/,
  );
  assert.match(
    mapExpenseSheetTicketDetailSource,
    /var totalAmountMST\s*=\s*headerExtras\.Count > 19 \? ToDecimal\(headerExtras\[19\]\) : null;/,
  );
  assert.match(
    mapExpenseSheetTicketDetailSource,
    /CurrencyCode\s*=\s*headerExtras\.Count > 3 \? headerExtras\[3\] : string\.Empty,[\s\S]*TotalAmount\s*=\s*totalAmountCurrency,[\s\S]*TotalAmountCurrency\s*=\s*totalAmountCurrency,[\s\S]*AmountMST\s*=\s*totalAmountMST,[\s\S]*TotalAmountMST\s*=\s*totalAmountMST,[\s\S]*ExchRate\s*=\s*headerExtras\.Count > 20 \? ToDecimal\(headerExtras\[20\]\) : null/,
  );
});

test("AX create mode 2 treats the persisted ticket as monetary authority", () => {
  const lineClearPosition = axCreateExpenseSheetSource.indexOf("line.clear();");
  const lineInsertPosition = axCreateExpenseSheetSource.indexOf("line.insert();");
  assert.ok(lineClearPosition > 0 && lineInsertPosition > lineClearPosition);
  const beforeClear = axCreateExpenseSheetSource.slice(0, lineClearPosition);
  const lineBuild = axCreateExpenseSheetSource.slice(lineClearPosition, lineInsertPosition);

  assert.match(
    beforeClear,
    /select firstonly forupdate ticketHeader[\s\S]*ticketHeader\.FileId\s*==\s*fileId[\s\S]*ticketHeader\.CreatedByUserId\s*==\s*axUserId/,
  );
  assert.match(
    beforeClear,
    /mode\s*==\s*#ModeAddLinesToExisting[\s\S]*ticketMonetarySnapshot\s*=\s*INDCRMExpenseSheetService::buildLinkedTicketMonetarySnapshot\(ticketHeader\);[\s\S]*if \(!conPeek\(ticketMonetarySnapshot, 1\)\)[\s\S]*ttsabort;[\s\S]*buildHeader\(false, conPeek\(ticketMonetarySnapshot, 2\), conNull\(\)\)/,
  );
  assert.match(
    beforeClear,
    /lineCurrencyCode\s*=\s*conPeek\(ticketMonetarySnapshot, 3\);[\s\S]*price\s*=\s*conPeek\(ticketMonetarySnapshot, 4\);[\s\S]*lineAmountMST\s*=\s*conPeek\(ticketMonetarySnapshot, 5\);[\s\S]*lineExchRate\s*=\s*conPeek\(ticketMonetarySnapshot, 6\);[\s\S]*hasLineAmountMST\s*=\s*true;[\s\S]*hasLineExchRate\s*=\s*true;/,
  );
  assert.doesNotMatch(
    beforeClear,
    /ticketHeader\.(?:CurrencyCode|TotalAmount|AmountMST|ExchRate)\s*=/,
    "Snapshot materialization must not mutate the locked ticket buffer",
  );
  assert.match(
    lineBuild,
    /line\.Price\s*=\s*price;[\s\S]*line\.Qty\s*=\s*qty;[\s\S]*line\.Currency\s*=\s*lineCurrencyCode[\s\S]*line\.Amount\s*=\s*line\.CalcAmount\(\);[\s\S]*if \(hasLineExchRate\)[\s\S]*line\.ExchRate\s*=\s*lineExchRate;[\s\S]*if \(hasLineAmountMST\)[\s\S]*line\.AmountMST\s*=\s*lineAmountMST;/,
  );
  assert.ok(
    lineBuild.indexOf("line.Currency = lineCurrencyCode") < lineBuild.indexOf("line.Amount   = line.CalcAmount();"),
    "The ticket currency must be assigned before currency-specific amount rounding",
  );
});

test("AX shared helper validates local and foreign ticket snapshots", () => {
  assert.match(
    linkedTicketMonetarySnapshotSource,
    /static container buildLinkedTicketMonetarySnapshot\(INDTicketInfoTable _ticketHeader\)/,
  );
  assert.doesNotMatch(
    linkedTicketMonetarySnapshotSource,
    /static server container buildLinkedTicketMonetarySnapshot/,
    "Called-from AX link paths must not force a tier transition inside TTS",
  );
  assert.match(
    linkedTicketMonetarySnapshotSource,
    /currencyCode\s*=\s*strUpr\(strLRTrim\(_ticketHeader\.CurrencyCode\)\);[\s\S]*resolveCurrencyCode\(currencyCode\);[\s\S]*if \(!currencyCode\)[\s\S]*return \[false,/,
  );
  assert.match(
    linkedTicketMonetarySnapshotSource,
    /totalAmount\s*=\s*Currency::amount\(_ticketHeader\.TotalAmount, currencyCode\);[\s\S]*if \(totalAmount <= 0\)[\s\S]*return \[false,/,
  );
  assert.match(
    linkedTicketMonetarySnapshotSource,
    /localCurrencyCode\s*=\s*CompanyInfo::standardCurrency\(\);[\s\S]*if \(currencyCode == localCurrencyCode\)[\s\S]*amountMST\s*=\s*Currency::amount\(totalAmount, localCurrencyCode\);[\s\S]*return \[true, '', currencyCode, totalAmount, amountMST, 100\];/,
  );
  assert.match(
    linkedTicketMonetarySnapshotSource,
    /amountMST\s*=\s*Currency::amount\(_ticketHeader\.AmountMST, localCurrencyCode\);[\s\S]*exchRate\s*=\s*_ticketHeader\.ExchRate;[\s\S]*if \(amountMST <= 0 \|\| exchRate <= 0\)[\s\S]*return \[false,[\s\S]*return \[true, '', currencyCode, totalAmount, amountMST, exchRate\];/,
  );
});

test("linked ticket inserts preserve the exact authoritative rate through table normalization", () => {
  assert.ok(
    insertExpenseSheetLineSource.indexOf("this.Amount = this.CalcAmount();") <
      insertExpenseSheetLineSource.indexOf(
        "linkedMonetarySnapshot = INDCRMExpenseSheetService::buildLinkedTicketMonetarySnapshot(linkedTicketSnapshot);",
      ),
    "Legacy callers must finish Qty/Price amount calculation before the authoritative comparison",
  );
  assert.match(
    insertExpenseSheetLineSource,
    /INDTicketInfoTable\s+linkedTicketSnapshot;[\s\S]*container\s+linkedMonetarySnapshot;[\s\S]*boolean\s+preserveLinkedTicketSnapshot;/,
  );
  assert.match(
    insertExpenseSheetLineSource,
    /linkedMonetarySnapshot\s*=\s*INDCRMExpenseSheetService::buildLinkedTicketMonetarySnapshot\(linkedTicketSnapshot\);/,
  );
  assert.match(
    insertExpenseSheetLineSource,
    /preserveLinkedTicketSnapshot\s*=[\s\S]{0,800}this\.Currency\s*==\s*conPeek\(linkedMonetarySnapshot, 3\)[\s\S]{0,800}this\.Amount\s*==\s*conPeek\(linkedMonetarySnapshot, 4\)[\s\S]{0,800}this\.AmountMST\s*==\s*conPeek\(linkedMonetarySnapshot, 5\)[\s\S]{0,800}this\.ExchRate\s*==\s*conPeek\(linkedMonetarySnapshot, 6\)/,
  );
  assert.match(
    insertExpenseSheetLineSource,
    /if \(preserveLinkedTicketSnapshot\)[\s\S]{0,300}this\.recalculateReimbursableAmount\(\);[\s\S]{0,300}else[\s\S]{0,300}this\.normalizeCurrencyAmounts\(amountMSTWasProvided\);/,
  );
  assert.match(
    insertExpenseSheetLineSource,
    /this\.syncLinkedTicket\(preserveLinkedTicketSnapshot\);/,
  );
  assert.match(
    syncLinkedTicketSource,
    /void syncLinkedTicket\(boolean _preserveCurrencySnapshot = false\)/,
  );
  assert.doesNotMatch(
    syncLinkedTicketSource,
    /buildLinkedTicketMonetarySnapshot\(ticketInfoTable\)/,
    "Table updates must not force a server helper call from inside their transaction",
  );
  assert.match(
    syncLinkedTicketSource,
    /_preserveCurrencySnapshot\s*=[\s\S]{0,1400}ticketInfoTable\.CurrencyCode\s*==\s*this\.Currency[\s\S]{0,1400}ticketInfoTable\.TotalAmount\s*==\s*this\.Amount[\s\S]{0,1400}ticketInfoTable\.AmountMST\s*==\s*this\.AmountMST[\s\S]{0,1400}ticketInfoTable\.ExchRate\s*==\s*this\.ExchRate/,
  );
  assert.match(
    syncLinkedTicketSource,
    /ticketInfoTable\.CurrencyCode\s*==\s*localCurrencyCode[\s\S]{0,500}ticketInfoTable\.ExchRate\s*==\s*100[\s\S]{0,500}ticketInfoTable\.AmountMST\s*==\s*Currency::amount\(ticketInfoTable\.TotalAmount, localCurrencyCode\)[\s\S]{0,500}ticketInfoTable\.CurrencyCode\s*!=\s*localCurrencyCode[\s\S]{0,500}ticketInfoTable\.AmountMST\s*>\s*0[\s\S]{0,500}ticketInfoTable\.ExchRate\s*>\s*0/,
  );
  assert.match(
    syncLinkedTicketSource,
    /if \(_preserveCurrencySnapshot\)[\s\S]{0,500}ticketInfoTable\.CurrencyCode\s*=\s*this\.Currency;[\s\S]{0,500}ticketInfoTable\.TotalAmount\s*=\s*this\.Amount;[\s\S]{0,500}ticketInfoTable\.ExchRate\s*=\s*this\.ExchRate;[\s\S]{0,500}ticketInfoTable\.AmountMST\s*=\s*this\.AmountMST;[\s\S]{0,500}else[\s\S]{0,300}ticketInfoTable\.normalizeCurrencyAmounts\(ticketInfoTable\.AmountMST != 0\);/,
  );
  assert.notEqual(
    (11.5 * 100) / 1.9,
    605.26,
    "Re-deriving a persisted rate from rounded amounts changes the authoritative ticket snapshot",
  );
  assert.match(
    axCreateExpenseSheetSource,
    /if \(mode == #ModeAddLinesToExisting && fileId\)[\s\S]{0,300}line\.recalculateReimbursableAmount\(\);[\s\S]{0,300}else[\s\S]{0,300}line\.normalizeCurrencyAmounts\(hasLineAmountMST\);/,
  );
  assert.match(
    axNativeTicketExpenseSheetLinkRunSource,
    /hojaGastosLine\.AmountMST\s*=\s*linkedAmountMST;[\s\S]{0,300}hojaGastosLine\.recalculateReimbursableAmount\(\);[\s\S]{0,500}hojaGastosLine\.insert\(\);/,
  );
});

test("native AX ticket linking validates the snapshot before mutating a line", () => {
  const lineMutationPosition = axNativeTicketExpenseSheetLinkRunSource.indexOf(
    "hojaGastosLine.clear();",
  );
  const lineInsertPosition = axNativeTicketExpenseSheetLinkRunSource.indexOf(
    "hojaGastosLine.insert();",
  );
  assert.ok(lineMutationPosition > 0 && lineInsertPosition > lineMutationPosition);

  const validationPrefix = axNativeTicketExpenseSheetLinkRunSource.slice(
    0,
    lineMutationPosition,
  );
  assert.match(
    validationPrefix,
    /ticketMonetarySnapshot\s*=\s*INDCRMExpenseSheetService::buildLinkedTicketMonetarySnapshot\(ticket\);/,
  );
  assert.match(
    validationPrefix,
    /if \(!conPeek\(ticketMonetarySnapshot, 1\)\)[\s\S]*warning\([\s\S]*conPeek\(ticketMonetarySnapshot, 2\)[\s\S]*continue;/,
  );
  assert.match(
    validationPrefix,
    /linkedCurrencyCode\s*=\s*conPeek\(ticketMonetarySnapshot, 3\);[\s\S]*linkedTotalAmount\s*=\s*conPeek\(ticketMonetarySnapshot, 4\);[\s\S]*linkedAmountMST\s*=\s*conPeek\(ticketMonetarySnapshot, 5\);[\s\S]*linkedExchRate\s*=\s*conPeek\(ticketMonetarySnapshot, 6\);/,
  );

  const beforeInsert = axNativeTicketExpenseSheetLinkRunSource.slice(0, lineInsertPosition);
  assert.match(
    beforeInsert,
    /hojaGastosLine\.Price\s*=\s*linkedTotalAmount;[\s\S]*hojaGastosLine\.Amount\s*=\s*linkedTotalAmount;[\s\S]*hojaGastosLine\.Currency\s*=\s*linkedCurrencyCode;[\s\S]*hojaGastosLine\.ExchRate\s*=\s*linkedExchRate;[\s\S]*hojaGastosLine\.AmountMST\s*=\s*linkedAmountMST;/,
  );
  assert.match(
    beforeInsert,
    /validationMessage\s*=\s*INDCRMExpenseSheetService::validateExpenseSheetLineForApi\(hojaGastosLine\);[\s\S]*if \(validationMessage\)[\s\S]*warning\([\s\S]*continue;/,
  );
});
