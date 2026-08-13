using BusinessPortal.Application;
using BusinessPortal.Domain;
using BusinessPortal.Infrastructure;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessPortal.IntegrationTests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class WorkflowAndExportTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Owner_can_bulk_delete_only_drafts()
    {
        var seed = await SeedWorkflowAsync();
        await using (var db = fixture.CreateContext())
        {
            db.TimeEntries.Add(new TimeEntry { OrganizationId = seed.EmployeeInfo.OrganizationId, ProjectId = seed.ProjectId, UserId = seed.EmployeeInfo.UserId, WorkDate = new(2026, 1, 13), Hours = 2, Description = "Second draft" });
            await db.SaveChangesAsync();
        }
        Guid[] ids;
        await using (var db = fixture.CreateContext())
            ids = await db.TimeEntries.Where(x => x.OrganizationId == seed.EmployeeInfo.OrganizationId).Select(x => x.Id).ToArrayAsync();

        var service = new TimeEntryService(fixture.CreateFactory(), new StubCurrentUser(seed.EmployeeInfo));
        await service.DeleteDraftsAsync(ids);

        await using var verification = fixture.CreateContext();
        Assert.False(await verification.TimeEntries.AnyAsync(x => ids.Contains(x.Id)));
    }

    [Fact]
    public async Task Draft_can_be_submitted_and_approved_with_audit()
    {
        var seed = await SeedWorkflowAsync();
        var employeeService = new TimeEntryService(fixture.CreateFactory(), new StubCurrentUser(seed.EmployeeInfo));
        var managerService = new TimeEntryService(fixture.CreateFactory(), new StubCurrentUser(seed.ManagerInfo));
        await employeeService.SubmitAsync(seed.EntryId, seed.ManagerInfo.UserId);
        var queue = await managerService.ApprovalsAsync(new(1, 20));
        var submitted = Assert.Single(queue.Items, x => x.Id == seed.EntryId);
        var details = await employeeService.GetDetailsAsync(seed.EntryId);
        Assert.Contains(details.Activities, x => x.Type == TimeEntryActivityType.Submitted && x.TargetName == "Manager");
        await managerService.ApproveAsync(submitted.Id, submitted.Version);

        await using var db = fixture.CreateContext();
        Assert.Equal(TimeEntryStatus.Approved, (await db.TimeEntries.FindAsync(seed.EntryId))!.Status);
        Assert.True(await db.AuditEntries.AnyAsync(x => x.EntityId == seed.EntryId.ToString() && x.Action == "TimeEntryApproved"));
        Assert.True(await db.Notifications.AnyAsync(x => x.RecipientUserId == seed.ManagerInfo.UserId && x.Type == NotificationType.TimeEntrySubmitted));
        Assert.True(await db.Notifications.AnyAsync(x => x.RecipientUserId == seed.EmployeeInfo.UserId && x.Type == NotificationType.TimeEntryApproved));
    }

    [Fact]
    public async Task Submission_is_visible_only_to_the_selected_manager()
    {
        var seed = await SeedWorkflowAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var otherManager = new ApplicationUser
        {
            Id = $"other-manager-{suffix}",
            UserName = $"other-manager-{suffix}",
            NormalizedUserName = $"OTHER-MANAGER-{suffix}",
            DisplayName = "Other Manager",
            OrganizationId = seed.EmployeeInfo.OrganizationId,
            SecurityStamp = suffix
        };
        await using (var db = fixture.CreateContext())
        {
            var managerRole = await db.Roles.SingleAsync(x => x.NormalizedName == "MANAGER");
            db.Users.Add(otherManager);
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = otherManager.Id, RoleId = managerRole.Id });
            await db.SaveChangesAsync();
        }

        var employeeService = new TimeEntryService(fixture.CreateFactory(), new StubCurrentUser(seed.EmployeeInfo));
        await employeeService.SubmitAsync(seed.EntryId, seed.ManagerInfo.UserId);
        var otherManagerInfo = new CurrentUserInfo(otherManager.Id, seed.EmployeeInfo.OrganizationId, seed.EmployeeInfo.OrganizationName, otherManager.DisplayName, new HashSet<string> { PortalRoles.Manager });
        var otherManagerService = new TimeEntryService(fixture.CreateFactory(), new StubCurrentUser(otherManagerInfo));

        Assert.DoesNotContain((await otherManagerService.ApprovalsAsync(new(1, 20))).Items, x => x.Id == seed.EntryId);
        var submitted = await employeeService.GetDetailsAsync(seed.EntryId);
        await Assert.ThrowsAsync<ForbiddenException>(() => otherManagerService.ReturnAsync(seed.EntryId, submitted.Version, "This was not assigned to me."));
    }

    [Fact]
    public async Task Notifications_are_user_scoped_and_can_be_marked_read()
    {
        var seed = await SeedWorkflowAsync();
        var employeeService = new TimeEntryService(fixture.CreateFactory(), new StubCurrentUser(seed.EmployeeInfo));
        await employeeService.SubmitAsync(seed.EntryId, seed.ManagerInfo.UserId);

        var managerNotifications = new NotificationService(fixture.CreateFactory(), new StubCurrentUser(seed.ManagerInfo));
        var managerFeed = await managerNotifications.GetAsync();
        var submitted = Assert.Single(managerFeed.Items, x => x.Type == NotificationType.TimeEntrySubmitted);
        Assert.Equal(1, managerFeed.UnreadCount);
        Assert.Equal($"/approvals?entry={seed.EntryId}", await managerNotifications.MarkReadAsync(submitted.Id));
        Assert.Equal(0, (await managerNotifications.GetAsync()).UnreadCount);

        var employeeNotifications = new NotificationService(fixture.CreateFactory(), new StubCurrentUser(seed.EmployeeInfo));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => employeeNotifications.MarkReadAsync(submitted.Id));
    }

    [Fact]
    public async Task Completing_project_notifies_other_active_organization_members()
    {
        var seed = await SeedWorkflowAsync();
        var projects = new ProjectService(fixture.CreateFactory(), new StubCurrentUser(seed.ManagerInfo));
        var input = await projects.GetAsync(seed.ProjectId);
        input.Status = ProjectStatus.Completed;
        await projects.SaveAsync(seed.ProjectId, input);

        await using var db = fixture.CreateContext();
        Assert.True(await db.Notifications.AnyAsync(x =>
            x.OrganizationId == seed.EmployeeInfo.OrganizationId
            && x.RecipientUserId == seed.EmployeeInfo.UserId
            && x.Type == NotificationType.ProjectCompleted
            && x.EntityId == seed.ProjectId.ToString()));
        Assert.False(await db.Notifications.AnyAsync(x =>
            x.RecipientUserId == seed.ManagerInfo.UserId
            && x.Type == NotificationType.ProjectCompleted
            && x.EntityId == seed.ProjectId.ToString()));
    }

    [Fact]
    public async Task Returned_entry_can_be_reopened_and_resubmitted()
    {
        var seed = await SeedWorkflowAsync();
        var employeeService = new TimeEntryService(fixture.CreateFactory(), new StubCurrentUser(seed.EmployeeInfo));
        var managerService = new TimeEntryService(fixture.CreateFactory(), new StubCurrentUser(seed.ManagerInfo));
        await employeeService.SubmitAsync(seed.EntryId, seed.ManagerInfo.UserId);
        var submitted = Assert.Single((await managerService.ApprovalsAsync(new(1, 20))).Items, x => x.Id == seed.EntryId);
        await managerService.ReturnAsync(submitted.Id, submitted.Version, "Please add delivery detail.");
        await employeeService.ReopenAsync(seed.EntryId);
        await employeeService.SubmitAsync(seed.EntryId, seed.ManagerInfo.UserId);
        await using var db = fixture.CreateContext();
        Assert.Equal(TimeEntryStatus.Submitted, (await db.TimeEntries.FindAsync(seed.EntryId))!.Status);
    }

    [Fact]
    public async Task Workflow_history_preserves_return_and_discussion_after_resubmission()
    {
        var seed = await SeedWorkflowAsync();
        var employeeService = new TimeEntryService(fixture.CreateFactory(), new StubCurrentUser(seed.EmployeeInfo));
        var managerService = new TimeEntryService(fixture.CreateFactory(), new StubCurrentUser(seed.ManagerInfo));

        await employeeService.SubmitAsync(seed.EntryId, seed.ManagerInfo.UserId);
        var submitted = Assert.Single((await managerService.ApprovalsAsync(new(1, 20))).Items, x => x.Id == seed.EntryId);
        await managerService.ReturnAsync(submitted.Id, submitted.Version, "Please add the delivery outcome.");
        await managerService.AddCommentAsync(seed.EntryId, "Include the client handoff reference as well.");
        await employeeService.ReopenAsync(seed.EntryId);
        await employeeService.AddCommentAsync(seed.EntryId, "Added the outcome and handoff reference.");
        await employeeService.SubmitAsync(seed.EntryId, seed.ManagerInfo.UserId);

        var details = await employeeService.GetDetailsAsync(seed.EntryId);
        Assert.False(details.IsHistorical);
        Assert.True(details.CanComment);
        Assert.Equal(
            [TimeEntryActivityType.Submitted, TimeEntryActivityType.Returned, TimeEntryActivityType.Comment, TimeEntryActivityType.Reopened, TimeEntryActivityType.Comment, TimeEntryActivityType.Submitted],
            details.Activities.Select(x => x.Type));
        Assert.Contains(details.Activities, x => x.Comment == "Please add the delivery outcome." && x.ActorName == "Manager");
        Assert.Contains(details.Activities, x => x.Comment == "Added the outcome and handoff reference." && x.ActorName == "Employee");

        await using var db = fixture.CreateContext();
        Assert.True(await db.Notifications.AnyAsync(x =>
            x.RecipientUserId == seed.EmployeeInfo.UserId
            && x.Type == NotificationType.TimeEntryCommented
            && x.TargetUrl == $"/my-time?entry={seed.EntryId}"));
    }

    [Fact]
    public async Task Assigned_employee_can_return_work_item_to_a_specific_manager_with_history()
    {
        var seed = await SeedWorkflowAsync();
        var workItem = new WorkItem
        {
            OrganizationId = seed.EmployeeInfo.OrganizationId,
            ProjectId = seed.ProjectId,
            Title = "Resolve implementation question",
            Description = "Needs a management decision.",
            Status = WorkItemStatus.InProgress,
            Priority = WorkItemPriority.High,
            AssignedToUserId = seed.EmployeeInfo.UserId,
            DueDate = new DateOnly(2026, 1, 20),
            EstimatedHours = 3
        };
        await using (var db = fixture.CreateContext())
        {
            db.WorkItems.Add(workItem);
            await db.SaveChangesAsync();
        }

        var employeeService = new WorkItemService(fixture.CreateFactory(), new StubCurrentUser(seed.EmployeeInfo));
        var beforeReturn = await employeeService.GetDetailsAsync(workItem.Id);
        Assert.False(beforeReturn.CanReturn);
        Assert.True(beforeReturn.IsAwaitingManagerComment);
        await Assert.ThrowsAsync<ConflictException>(() => employeeService.ReturnAsync(workItem.Id, seed.ManagerInfo.UserId, "Please confirm the client-facing scope."));

        var managerService = new WorkItemService(fixture.CreateFactory(), new StubCurrentUser(seed.ManagerInfo));
        await managerService.AddCommentAsync(workItem.Id, "Return this to me if the client-facing scope still needs a decision.");
        var afterManagerComment = await employeeService.GetDetailsAsync(workItem.Id);
        Assert.True(afterManagerComment.CanReturn);
        Assert.False(afterManagerComment.IsAwaitingManagerComment);
        Assert.True(afterManagerComment.CanComment);

        await employeeService.AddCommentAsync(workItem.Id, "The client requirement is still ambiguous.");
        var afterEmployeeComment = await employeeService.GetDetailsAsync(workItem.Id);
        Assert.False(afterEmployeeComment.CanComment);
        Assert.False(afterEmployeeComment.CanReturn);
        Assert.True(afterEmployeeComment.IsAwaitingReply);
        await Assert.ThrowsAsync<ConflictException>(() => employeeService.AddCommentAsync(workItem.Id, "A duplicate follow-up."));
        await Assert.ThrowsAsync<ConflictException>(() => employeeService.ReturnAsync(workItem.Id, seed.ManagerInfo.UserId, "A return without a new manager reply."));

        await managerService.AddCommentAsync(workItem.Id, "Please return it and I will confirm the final scope.");

        await employeeService.ReturnAsync(workItem.Id, seed.ManagerInfo.UserId, "Please confirm the client-facing scope.");
        await Assert.ThrowsAsync<ConflictException>(() => employeeService.AddCommentAsync(workItem.Id, "A follow-up immediately after returning."));

        var details = await managerService.GetDetailsAsync(workItem.Id);
        var returned = Assert.Single(details.Activities, x => x.Type == WorkItemActivityType.Returned);
        Assert.Equal("Manager", returned.TargetName);
        Assert.Equal("Please confirm the client-facing scope.", returned.Comment);
        Assert.True(details.CanManage);
        Assert.Equal(3, details.Activities.Count(x => x.Type == WorkItemActivityType.Comment));
        Assert.True(details.CanComment);

        await using var verification = fixture.CreateContext();
        var saved = await verification.WorkItems.SingleAsync(x => x.Id == workItem.Id);
        Assert.Equal(seed.ManagerInfo.UserId, saved.AssignedToUserId);
        Assert.Equal(WorkItemStatus.Open, saved.Status);
        Assert.True(await verification.Notifications.AnyAsync(x =>
            x.RecipientUserId == seed.ManagerInfo.UserId
            && x.Type == NotificationType.WorkItemReturned
            && x.EntityId == workItem.Id.ToString()));
    }

    [Fact]
    public async Task Approved_time_entry_is_historical_and_rejects_new_comments()
    {
        var seed = await SeedWorkflowAsync();
        var employeeService = new TimeEntryService(fixture.CreateFactory(), new StubCurrentUser(seed.EmployeeInfo));
        var managerService = new TimeEntryService(fixture.CreateFactory(), new StubCurrentUser(seed.ManagerInfo));

        await employeeService.SubmitAsync(seed.EntryId, seed.ManagerInfo.UserId);
        var submitted = Assert.Single((await managerService.ApprovalsAsync(new(1, 20))).Items, x => x.Id == seed.EntryId);
        await managerService.ApproveAsync(submitted.Id, submitted.Version);

        var details = await employeeService.GetDetailsAsync(seed.EntryId);
        Assert.True(details.IsHistorical);
        Assert.False(details.CanComment);
        Assert.Equal(TimeEntryActivityType.Approved, details.Activities[^1].Type);
        await Assert.ThrowsAsync<ConflictException>(() => employeeService.AddCommentAsync(seed.EntryId, "Late change"));
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
        Assert.StartsWith("'", rows[4].Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>().ElementAt(7).InnerText);
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
        var managerRole = await db.Roles.SingleOrDefaultAsync(x => x.NormalizedName == "MANAGER");
        if (managerRole is null)
        {
            managerRole = new IdentityRole(PortalRoles.Manager) { Id = $"role-manager-{suffix}", NormalizedName = PortalRoles.Manager.ToUpperInvariant() };
            db.Roles.Add(managerRole);
        }
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = manager.Id, RoleId = managerRole.Id });
        await db.SaveChangesAsync();
        return new(
            entry.Id,
            project.Id,
            new(employee.Id, org.Id, org.Name, employee.DisplayName, new HashSet<string> { PortalRoles.Employee }),
            new(manager.Id, org.Id, org.Name, manager.DisplayName, new HashSet<string> { PortalRoles.Manager }));
    }

    private sealed record WorkflowSeed(Guid EntryId, Guid ProjectId, CurrentUserInfo EmployeeInfo, CurrentUserInfo ManagerInfo);
}
