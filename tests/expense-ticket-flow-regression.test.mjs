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
const linkedTicketCurrencyFieldsSource = sourceBetween(
  controllerSource,
  "private static void AppendLinkedTicketLineCurrencyFields(",
  "// Returns only positive currency values because AX rejects empty or zero conversion data.",
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
const axLinkLineTicketSource = sourceBetween(
  expenseSheetServiceSource,
  "SOURCE #linkExpenseSheetLineTicket",
  "SOURCE #unlinkExpenseSheetLineTicket",
).replace(/^\s*#/gm, "");
const axUnlinkLineTicketSource = sourceBetween(
  expenseSheetServiceSource,
  "SOURCE #unlinkExpenseSheetLineTicket",
  "    ENDMETHODS",
).replace(/^\s*#/gm, "");
const axRefreshTicketStatusSource = sourceBetween(
  expenseSheetServiceSource,
  "SOURCE #refreshTicketStatusByFileId",
  "SOURCE #renderExpenseSheetTemplate",
).replace(/^\s*#/gm, "");
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
const realProjectResolverSource = sourceBetween(
  expenseSheetLineSource,
  "SOURCE #resolveRealProjectId",
  "SOURCE #InitFromHojaGastosTable",
).replace(/^\s*#/gm, "");
const initLineFromSheetSource = sourceBetween(
  expenseSheetLineSource,
  "SOURCE #InitFromHojaGastosTable",
  "SOURCE #InitFromPreviousLine",
).replace(/^\s*#/gm, "");
const axCreateExpenseSheetSource = sourceBetween(
  expenseSheetServiceSource,
  "SOURCE #createExpenseSheet",
  "SOURCE #createExpenseSheetTicket",
).replace(/^\s*#/gm, "");
const axUpdateExpenseSheetHeaderSource = sourceBetween(
  expenseSheetServiceSource,
  "SOURCE #updateExpenseSheetHeader",
  "SOURCE #updateExpenseSheetLine",
).replace(/^\s*#/gm, "");
const axUpdateExpenseSheetLineSource = sourceBetween(
  expenseSheetServiceSource,
  "SOURCE #updateExpenseSheetLine",
  "    ENDMETHODS",
).replace(/^\s*#/gm, "");
const validateLineProjectFieldSource = sourceBetween(
  expenseSheetLineSource,
  "SOURCE #validateField",
  "SOURCE #validateWrite",
).replace(/^\s*#/gm, "");
const validateLineProjectWriteSource = sourceBetween(
  expenseSheetLineSource,
  "SOURCE #validateWrite",
  "SOURCE #Find",
).replace(/^\s*#/gm, "");
const defaultLineProjectSource = sourceBetween(
  expenseSheetHeaderSource,
  "SOURCE #defaultProjectForNewLine",
  "SOURCE #isVariousProjectDefault",
).replace(/^\s*#/gm, "");
const recalculateHeaderProjectSource = sourceBetween(
  expenseSheetHeaderSource,
  "SOURCE #recalculateProjectFromLines",
  "SOURCE #markHeaderVariousFromLine",
).replace(/^\s*#/gm, "");
const propagateHeaderProjectSource = sourceBetween(
  expenseSheetServiceSource,
  "SOURCE #propagateExpenseSheetProjectDefault",
  "SOURCE #propagateExpenseSheetReimbursableExpense",
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

// Mirrors the nullable header project-intent fallback used by legacy API callers.
function resolveHeaderProjectProvided(flag, projId) {
  return flag ?? (projId !== null);
}

// The optional intent position is emitted only when a new client sends the flag.
function hasExplicitLineProjectIntent(flag) {
  return flag !== null && flag !== undefined;
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
    /if \(legacyProjectContract && !projId\)[\s\S]*if \(!header\.isVariousProjectDefault\(\)\)[\s\S]*projId\s*=\s*header\.ProjId;[\s\S]*else[\s\S]*projectProvided\s*=\s*false;/,
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
    propagateHeaderProjectSource,
    /updated\s*=\s*header\.updateProjectDefaultInLines\(projectId\);/,
  );
});

test("unchanged historical line projects remain editable after project closure", () => {
  const strictEligibilityPredicate =
    /if \(this\.ProjIdHornos\s*&&\s*\(!this\.RecId \|\| this\.ProjIdHornos != this\.orig\(\)\.ProjIdHornos\)\s*&&\s*!CRMHojaGastosLine::resolveEligibleProjectId\(this\.ProjIdHornos\)\)/;

  assert.match(validateLineProjectFieldSource, strictEligibilityPredicate);
  assert.match(validateLineProjectWriteSource, strictEligibilityPredicate);
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

test("expense lines never inherit a mixed or missing project id", () => {
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
    expenseSheetLineSource,
    /this\.ProjIdHornos\s*=\s*CRMHojaGastosLine::resolveRealProjectId\(_hojaGastosLine\.ProjIdHornos\);/,
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
  assert.match(defaultLineProjectSource, /order by createdDate desc, createdTime desc, RecId desc/);
  assert.match(defaultLineProjectSource, /lastLine\.UserId\s*==\s*this\.UserId/);
  assert.match(recalculateHeaderProjectSource, /lineProjectId\s*!=\s*commonProjectId/);
  assert.match(
    recalculateHeaderProjectSource,
    /calculatedProjectId\s*=\s*purchParameters\.INDProjIdVarious/,
  );
  assert.match(
    propagateHeaderProjectSource,
    /conLen\(_data\)\s*>=\s*5[\s\S]*conPeek\(_data,\s*5\)/,
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
    /quickCreateForm\.ProjectId,\s*quickCreateForm\.ProjectProvided,\s*true,/,
  );
  assert.match(
    controllerSource,
    /targetInfo\.ProjId,\s*false,\s*false,\s*out var linkMessage/,
  );
  assert.match(
    linkTicketToExpenseSheetSource,
    /string projectId,\s*bool projectProvided,\s*bool fallbackMissingCurrencyValues/,
  );
  assert.match(
    linkTicketToExpenseSheetSource,
    /lineCon\.Append\(projectProvided \? \(projectId \?\? string\.Empty\)\.Trim\(\) : string\.Empty\);/,
  );

  const currencyFieldsPosition = linkTicketToExpenseSheetSource.indexOf(
    "AppendLinkedTicketLineCurrencyFields(lineCon, ticketDetail, fallbackMissingCurrencyValues);",
  );
  const projectFlagPosition = linkTicketToExpenseSheetSource.indexOf(
    "lineCon.Append(ToAxBool(projectProvided));",
  );
  assert.ok(currencyFieldsPosition >= 0 && projectFlagPosition > currencyFieldsPosition);

  const stableFieldAppends = linkedTicketCurrencyFieldsSource.match(/lineCon\.Append\(/g) ?? [];
  assert.equal(stableFieldAppends.length, 4);
  assert.doesNotMatch(
    linkedTicketCurrencyFieldsSource,
    /if \(!fallbackMissingCurrencyValues\)\s*return;/,
  );
});
