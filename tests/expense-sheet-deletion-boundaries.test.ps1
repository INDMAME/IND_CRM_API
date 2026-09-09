[CmdletBinding()]
param([string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $AssemblyPath = Join-Path $PSScriptRoot '..\bin\x86\Release\IND_CRM_API.exe'
}

# Load the actual release assembly in the same architecture as the COM host.
if ([IntPtr]::Size -ne 4) {
    & "$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $PSCommandPath -AssemblyPath $AssemblyPath
    if ($LASTEXITCODE -ne 0) { throw 'Deletion boundary tests failed.' }
    return
}
$artifact = (Resolve-Path -LiteralPath $AssemblyPath).Path
$artifactDirectory = Split-Path -Parent $artifact
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    try { $dependencyName = [Reflection.AssemblyName]::new($eventArgs.Name).Name }
    catch { return $null }
    $candidate = Join-Path $artifactDirectory ($dependencyName + '.dll')
    if (Test-Path -LiteralPath $candidate) { return [Reflection.Assembly]::LoadFrom($candidate) }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
try {
    [void][Reflection.Assembly]::LoadFrom($artifact)
    $references = @($artifact, (Join-Path $artifactDirectory 'System.Web.Http.dll'),
        (Join-Path $artifactDirectory 'Newtonsoft.Json.dll'), 'System.dll', 'System.Core.dll', 'System.Net.Http.dll')
    $source = @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Web.Http;
using System.Web.Http.Results;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Controllers.CRM;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using Newtonsoft.Json;

// Exercises the real adapter's pure boundaries without authenticating or touching AX/Azure.
public static class ExpenseDeletionBoundaryTests
{
    private static readonly Type Controller = typeof(CrmExpenseSheetDeletionController);
    private static int checks;
    private sealed class EmptyController : ApiController { }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        checks++;
        Console.WriteLine("PASS: " + name);
    }

    private static void Reject(Action action, string name)
    {
        bool rejected = false;
        try { action(); } catch (TargetInvocationException) { rejected = true; }
        Assert(rejected, name);
    }

    private static ExpenseSheetDetailDto Sheet(params ExpenseSheetLineDto[] lines)
    {
        return new ExpenseSheetDetailDto { OwnerAxUserId = "owner", ExpenseSheetStatus = 0, Lines = lines.ToList() };
    }

    private static ExpenseSheetLineDto Line(string file, bool? origin)
    {
        return new ExpenseSheetLineDto { FileId = file, Ticket = false, CreatedFromTicket = origin };
    }

    private static ExpenseSheetTicketDetailDto Ticket(string file)
    {
        return new ExpenseSheetTicketDetailDto { FileId = file, OwnerAxUserId = "owner", Status = 0, UrlFile = "saved-file" };
    }

    private static DeletionSnapshot Snapshot(ExpenseSheetDetailDto sheet, Func<string, ExpenseSheetTicketDetailDto> read,
        Action<string> validate)
    {
        return (DeletionSnapshot)Controller.GetMethod("BuildSnapshot", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { sheet, "owner", read, validate });
    }

    public static void Run()
    {
        int reads = 0, validations = 0;
        Func<string, ExpenseSheetTicketDetailDto> read = id => { reads++; return Ticket(id); };
        Action<string> validate = url => { validations++; Assert(url == "saved-file", "original URL validated before deletion"); };
        var manual = Snapshot(Sheet(Line("manual", false)), read, validate);
        Assert(manual.Tickets.Count == 0 && reads == 0 && manual.PreservedManualTicketCount == 1, "manual ticket and file preserved");
        var paperLine = Line("paper", false); paperLine.Ticket = true;
        var paper = Snapshot(Sheet(paperLine), read, validate);
        Assert(paper.Tickets.Count == 0 && reads == 0, "legacy paper flag never authorizes digital cleanup");
        var mixed = Snapshot(Sheet(Line("shared", true), Line("SHARED", false)), read, validate);
        Assert(mixed.Tickets.Count == 0 && reads == 0, "mixed origin associations preserve shared ticket");
        var legacy = Snapshot(Sheet(Line("origin", true), Line("legacy", null)), read, validate);
        Assert(!legacy.MetadataAvailable && legacy.Tickets.Count == 0 && reads == 0, "legacy metadata preserves all assets");
        var original = Snapshot(Sheet(Line("origin", true), Line("ORIGIN", true), Line(null, false)), read, validate);
        Assert(original.MetadataAvailable && original.Tickets.Count == 1 && reads == 1 && validations == 1,
            "origin ticket is inventoried once and a manual line without a file is preserved");
        var empty = Snapshot(Sheet(), read, validate);
        Assert(empty.Tickets.Count == 0 && empty.MetadataAvailable, "empty draft remains deletable");

        var locked = Sheet(); locked.Voucher = "posted";
        Reject(() => Snapshot(locked, read, validate), "voucher blocks deletion");
        foreach (int status in new[] { 1, 2, 3, 4 }) {
            var nonDraft = Sheet(); nonDraft.ExpenseSheetStatus = status;
            Reject(() => Snapshot(nonDraft, read, validate), "non-draft status blocks deletion " + status);
        }
        var otherOwner = Sheet(); otherOwner.OwnerAxUserId = "someone-else";
        Reject(() => Snapshot(otherOwner, read, validate), "sheet owner mismatch rejected");
        var missingLines = Sheet(); missingLines.Lines = null;
        Reject(() => Snapshot(missingLines, read, validate), "missing line inventory rejected");
        Reject(() => Snapshot(Sheet(Line("origin", true)), id => null, validate), "missing origin ticket prevents partial sheet delete");
        Reject(() => Snapshot(Sheet(Line("origin", true)), id => { var t = Ticket(id); t.OwnerAxUserId = "other"; return t; }, validate),
            "ticket owner mismatch rejected");
        Reject(() => Snapshot(Sheet(Line("origin", true)), read, url => { throw new InvalidOperationException(); }),
            "invalid blob prevents initial sheet deletion");

        var saved = new DeletionTicket { FileId = "origin", BlobUrl = "saved-file" };
        var checkTicket = Controller.GetMethod("ValidateTicketForCleanup", BindingFlags.Static | BindingFlags.NonPublic);
        checkTicket.Invoke(null, new object[] { Ticket("origin"), saved, "owner" });
        Assert(true, "unchanged pending own ticket may enter guarded AX delete");
        var relinked = Ticket("origin"); relinked.Status = 1;
        Reject(() => checkTicket.Invoke(null, new object[] { relinked, saved, "owner" }), "relinked ticket retained");
        var replacedFile = Ticket("origin"); replacedFile.UrlFile = "replacement";
        Reject(() => checkTicket.Invoke(null, new object[] { replacedFile, saved, "owner" }), "replacement file retained");

        var readItem = Controller.GetMethod("ReadItem", BindingFlags.Static | BindingFlags.NonPublic)
            .MakeGenericMethod(typeof(ExpenseSheetDetailDto));
        var shell = new EmptyController();
        Func<HttpStatusCode, string, IHttpActionResult> failure = (status, code) =>
            new NegotiatedContentResult<IndApiResponse<object>>(status,
                new IndApiResponse<object> { Success = false, ErrorCode = code }, shell);
        var notFound = readItem.Invoke(null, new object[] { failure(HttpStatusCode.NotFound, "CRM_EXPENSESHEET_NOT_FOUND"), "CRM_EXPENSESHEET_NOT_FOUND" });
        Assert(notFound == null, "only authoritative exact missing-sheet result resumes ambiguous commit");
        Reject(() => readItem.Invoke(null, new object[] { failure(HttpStatusCode.Forbidden, "AUTH_FORBIDDEN"), "CRM_EXPENSESHEET_NOT_FOUND" }),
            "forbidden never interpreted as sheet missing");
        Reject(() => readItem.Invoke(null, new object[] { failure(HttpStatusCode.InternalServerError, "AX_COM_ERROR"), "CRM_EXPENSESHEET_NOT_FOUND" }),
            "AX outage never interpreted as sheet missing");
        Reject(() => readItem.Invoke(null, new object[] { failure(HttpStatusCode.NotFound, "OTHER_NOT_FOUND"), "CRM_EXPENSESHEET_NOT_FOUND" }),
            "unrelated 404 never authorizes cleanup");
        var emptySuccess = new OkNegotiatedContentResult<IndPagedResponse<ExpenseSheetDetailDto>>(
            new IndPagedResponse<ExpenseSheetDetailDto> { Success = true, Items = new List<ExpenseSheetDetailDto>() }, shell);
        Reject(() => readItem.Invoke(null, new object[] { emptySuccess, "CRM_EXPENSESHEET_NOT_FOUND" }), "malformed success cannot authorize cleanup");
        var progressJson = JsonConvert.SerializeObject(new ExpenseSheetDeletionProgressDto { Exists = true, DeleteAttempted = true, SheetDeleted = true });
        Assert(!progressJson.Contains("FileId") && !progressJson.Contains("Actor") && !progressJson.Contains("BlobUrl"), "public progress has no inventory or identity");
        Console.WriteLine("Deletion boundary checks passed: " + checks);
    }
}
'@
    $compiler = New-Object Microsoft.CSharp.CSharpCodeProvider
    $parameters = New-Object System.CodeDom.Compiler.CompilerParameters
    $parameters.GenerateInMemory = $true
    foreach ($reference in $references) { [void]$parameters.ReferencedAssemblies.Add($reference) }
    $compiled = $compiler.CompileAssemblyFromSource($parameters, $source)
    if ($compiled.Errors.HasErrors) { throw ($compiled.Errors | Out-String) }
    $compiled.CompiledAssembly.GetType('ExpenseDeletionBoundaryTests').GetMethod('Run').Invoke($null, @())
    $compiler.Dispose()
}
finally { [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver) }
