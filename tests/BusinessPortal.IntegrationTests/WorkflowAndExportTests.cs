using BusinessPortal.Application;
using BusinessPortal.Domain;
using BusinessPortal.Infrastructure;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.EntityFrameworkCore;

namespace BusinessPortal.IntegrationTests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class WorkflowAndExportTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Draft_can_be_submitted_and_approved_with_audit()
    {
        var seed = await SeedWorkflowAsync();
        var employeeService = new TimeEntryService(fixture.CreateFactory(), new StubCurrentUser(seed.EmployeeInfo));
        var managerService = new TimeEntryService(fixture.CreateFactory(), new StubCurrentUser(seed.ManagerInfo));
        await employeeService.SubmitAsync(seed.EntryId);
        var queue = await managerService.ApprovalsAsync(new(1, 20));
        var submitted = Assert.Single(queue.Items, x => x.Id == seed.EntryId);
        await managerService.ApproveAsync(submitted.Id, submitted.Version);

        await using var db = fixture.CreateContext();
        Assert.Equal(TimeEntryStatus.Approved, (await db.TimeEntries.FindAsync(seed.EntryId))!.Status);
        Assert.True(await db.AuditEntries.AnyAsync(x => x.EntityId == seed.EntryId.ToString() && x.Action == "TimeEntryApproved"));
    }

    [Fact]
    public async Task Rejected_entry_can_be_reopened_and_resubmitted()
    {
        var seed = await SeedWorkflowAsync();
        var employeeService = new TimeEntryService(fixture.CreateFactory(), new StubCurrentUser(seed.EmployeeInfo));
        var managerService = new TimeEntryService(fixture.CreateFactory(), new StubCurrentUser(seed.ManagerInfo));
        await employeeService.SubmitAsync(seed.EntryId);
        var submitted = Assert.Single((await managerService.ApprovalsAsync(new(1, 20))).Items, x => x.Id == seed.EntryId);
        await managerService.RejectAsync(submitted.Id, submitted.Version, "Please add delivery detail.");
        await employeeService.ReopenAsync(seed.EntryId);
        await employeeService.SubmitAsync(seed.EntryId);
        await using var db = fixture.CreateContext();
        Assert.Equal(TimeEntryStatus.Submitted, (await db.TimeEntries.FindAsync(seed.EntryId))!.Status);
    }

    [Fact]
    public void Excel_export_has_workbook_headers_and_data_row()
    {
        var filter = new ReportFilter(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        var bytes = ExcelReportWriter.Create(
            [new ReportRow(new DateOnly(2026, 1, 12), "Client", "Project", "Employee", 7.5m, "=unsafe text", TimeEntryStatus.Approved)],
            filter,
            "Test Organization");
        using var stream = new MemoryStream(bytes);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("Workbook part is missing.");
        var workbook = workbookPart.Workbook ?? throw new InvalidOperationException("Workbook is missing.");
        var sheet = Assert.Single(workbook.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>());
        Assert.Equal("Time report", sheet.Name?.Value);
        var worksheet = workbookPart.WorksheetParts.Single().Worksheet ?? throw new InvalidOperationException("Worksheet is missing.");
        var rows = worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Row>().ToList();
        Assert.Equal(5, rows.Count);
        Assert.StartsWith("'", rows[4].Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>().ElementAt(5).InnerText);
    }

    private async Task<WorkflowSeed> SeedWorkflowAsync()
    {
        await using var db = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N");
        var org = new Organization { Name = $"Workflow {suffix}", Slug = $"workflow-{suffix}" };
        var employee = new ApplicationUser { Id = $"employee-{suffix}", UserName = $"employee-{suffix}", NormalizedUserName = $"EMPLOYEE-{suffix}", DisplayName = "Employee", OrganizationId = org.Id, SecurityStamp = suffix };
        var manager = new ApplicationUser { Id = $"manager-{suffix}", UserName = $"manager-{suffix}", NormalizedUserName = $"MANAGER-{suffix}", DisplayName = "Manager", OrganizationId = org.Id, SecurityStamp = suffix };
        var client = new Client { OrganizationId = org.Id, Name = "Client" };
        var project = new Project { OrganizationId = org.Id, ClientId = client.Id, Name = "Project", Code = $"P-{suffix[..6]}", Status = ProjectStatus.Active, StartDate = new DateOnly(2026, 1, 1) };
        var entry = new TimeEntry { OrganizationId = org.Id, ProjectId = project.Id, UserId = employee.Id, WorkDate = new DateOnly(2026, 1, 12), Hours = 8, Description = "Implemented tenant-safe workflow." };
        db.AddRange(org, employee, manager, client, project, entry);
        await db.SaveChangesAsync();
        return new(
            entry.Id,
            new(employee.Id, org.Id, org.Name, employee.DisplayName, new HashSet<string> { PortalRoles.Employee }),
            new(manager.Id, org.Id, org.Name, manager.DisplayName, new HashSet<string> { PortalRoles.Manager }));
    }

    private sealed record WorkflowSeed(Guid EntryId, CurrentUserInfo EmployeeInfo, CurrentUserInfo ManagerInfo);
}
