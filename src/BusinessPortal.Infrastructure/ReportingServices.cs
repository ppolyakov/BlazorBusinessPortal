using BusinessPortal.Application;
using BusinessPortal.Domain;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;

namespace BusinessPortal.Infrastructure;

internal sealed class DashboardService(IDbContextFactory<ApplicationDbContext> factory, ICurrentUser currentUser)
    : PortalService(factory, currentUser), IDashboardService
{
    public async Task<DashboardModel> GetAsync(CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1);
        var activeClients = await db.Clients.CountAsync(x => x.OrganizationId == user.OrganizationId && x.Status == ClientStatus.Active, cancellationToken);
        var activeProjects = await db.Projects.CountAsync(x => x.OrganizationId == user.OrganizationId && x.Status == ProjectStatus.Active, cancellationToken);
        var monthHours = await db.TimeEntries.Where(x => x.OrganizationId == user.OrganizationId && x.WorkDate >= monthStart && x.WorkDate < monthEnd).SumAsync(x => (decimal?)x.Hours, cancellationToken) ?? 0;
        var awaiting = user.CanManage ? await db.TimeEntries.CountAsync(x => x.OrganizationId == user.OrganizationId && x.Status == TimeEntryStatus.Submitted && x.UserId != user.UserId, cancellationToken) : 0;
        var chart = await (from entry in db.TimeEntries.AsNoTracking()
                           join project in db.Projects.AsNoTracking() on entry.ProjectId equals project.Id
                           where entry.OrganizationId == user.OrganizationId && entry.WorkDate >= monthStart && entry.WorkDate < monthEnd
                           group entry by project.Name into groupRows
                           orderby groupRows.Sum(x => x.Hours) descending
                           select new ChartItem(groupRows.Key, groupRows.Sum(x => x.Hours))).Take(6).ToListAsync(cancellationToken);
        var activity = await (from audit in db.AuditEntries.AsNoTracking()
                              join actor in db.Users.AsNoTracking() on audit.UserId equals actor.Id
                              where audit.OrganizationId == user.OrganizationId
                              orderby audit.OccurredAtUtc descending
                              select new AuditListItem(
                                  audit.Id,
                                  actor.DisplayName,
                                  audit.Action,
                                  audit.EntityType,
                                  audit.EntityId,
                                  audit.Summary,
                                  audit.OccurredAtUtc,
                                  actor.Id,
                                  actor.AvatarImage == null ? null : "/avatars/" + actor.Id))
                              .Take(6).ToListAsync(cancellationToken);
        var upcoming = await (from item in db.WorkItems.AsNoTracking()
                              join project in db.Projects.AsNoTracking() on item.ProjectId equals project.Id
                              join assigned in db.Users.AsNoTracking() on item.AssignedToUserId equals assigned.Id into assignments
                              from assigned in assignments.DefaultIfEmpty()
                              where item.OrganizationId == user.OrganizationId && item.Status != WorkItemStatus.Done && item.DueDate >= today
                              orderby item.DueDate
                              select new WorkItemListItem(
                                  item.Id,
                                  project.Id,
                                  project.Name,
                                  item.Title,
                                  item.Status,
                                  item.Priority,
                                  item.AssignedToUserId,
                                  assigned == null ? null : assigned.DisplayName,
                                  item.DueDate,
                                  item.EstimatedHours,
                                  assigned == null || assigned.AvatarImage == null ? null : "/avatars/" + assigned.Id))
                              .Take(6).ToListAsync(cancellationToken);
        return new(activeClients, activeProjects, monthHours, awaiting, chart, activity, upcoming);
    }
}

internal sealed class ReportService(IDbContextFactory<ApplicationDbContext> factory, ICurrentUser currentUser)
    : PortalService(factory, currentUser), IReportService
{
    private const int MaximumExportRows = 10_000;

    public async Task<ReportModel> GetAsync(ReportFilter filter, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        if (!user.CanManage) throw new ForbiddenException();
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var query = BuildQuery(db, user.OrganizationId, filter);
        var totalCount = await query.CountAsync(cancellationToken);
        var totalHours = await query.SumAsync(x => (decimal?)x.Entry.Hours, cancellationToken) ?? 0;
        var totals = await query.GroupBy(x => x.Project.Name).OrderByDescending(x => x.Sum(y => y.Entry.Hours))
            .Select(x => new ChartItem(x.Key, x.Sum(y => y.Entry.Hours))).Take(12).ToListAsync(cancellationToken);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var page = Math.Max(1, filter.Page);
        var rows = await query.OrderByDescending(x => x.Entry.WorkDate).ThenBy(x => x.Project.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new ReportRow(
                x.Entry.WorkDate,
                x.Client.Name,
                x.Project.Name,
                x.User.DisplayName,
                x.Entry.Hours,
                x.Entry.Description,
                x.Entry.Status,
                x.User.Id,
                x.User.AvatarImage == null ? null : "/avatars/" + x.User.Id))
            .ToListAsync(cancellationToken);
        return new(totals, new(rows, totalCount, page, pageSize), totalHours);
    }

    public async Task<(byte[] Content, string FileName)> ExportAsync(ReportFilter filter, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        if (!user.CanManage) throw new ForbiddenException();
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var rows = await BuildQuery(db, user.OrganizationId, filter).OrderByDescending(x => x.Entry.WorkDate)
            .Take(MaximumExportRows + 1)
            .Select(x => new ReportRow(
                x.Entry.WorkDate,
                x.Client.Name,
                x.Project.Name,
                x.User.DisplayName,
                x.Entry.Hours,
                x.Entry.Description,
                x.Entry.Status,
                x.User.Id,
                x.User.AvatarImage == null ? null : "/avatars/" + x.User.Id))
            .ToListAsync(cancellationToken);
        if (rows.Count > MaximumExportRows) throw new ConflictException($"Export is limited to {MaximumExportRows:N0} rows. Narrow the filters.");
        var content = ExcelReportWriter.Create(rows, filter, user.OrganizationName);
        AddAudit(db, user, "ReportExported", "Report", $"{filter.From:yyyyMMdd}-{filter.To:yyyyMMdd}", $"Exported {rows.Count} filtered report rows.");
        await db.SaveChangesAsync(cancellationToken);
        return (content, $"business-portal-report-{filter.From:yyyyMMdd}-{filter.To:yyyyMMdd}.xlsx");
    }

    private static IQueryable<ReportQueryRow> BuildQuery(ApplicationDbContext db, Guid organizationId, ReportFilter filter)
    {
        var query = from entry in db.TimeEntries.AsNoTracking()
                    join project in db.Projects.AsNoTracking() on entry.ProjectId equals project.Id
                    join client in db.Clients.AsNoTracking() on project.ClientId equals client.Id
                    join user in db.Users.AsNoTracking() on entry.UserId equals user.Id
                    where entry.OrganizationId == organizationId && project.OrganizationId == organizationId
                       && entry.WorkDate >= filter.From && entry.WorkDate <= filter.To
                    select new ReportQueryRow { Entry = entry, Project = project, Client = client, User = user };
        if (filter.ClientId.HasValue) query = query.Where(x => x.Client.Id == filter.ClientId);
        if (filter.ProjectId.HasValue) query = query.Where(x => x.Project.Id == filter.ProjectId);
        if (filter.UserId is not null) query = query.Where(x => x.User.Id == filter.UserId);
        return query;
    }

    private sealed class ReportQueryRow
    {
        public required TimeEntry Entry { get; init; }
        public required Project Project { get; init; }
        public required Client Client { get; init; }
        public required ApplicationUser User { get; init; }
    }
}

internal sealed class AuditService(IDbContextFactory<ApplicationDbContext> factory, ICurrentUser currentUser)
    : PortalService(factory, currentUser), IAuditService
{
    public async Task<AuditFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        if (!user.IsInRole(PortalRoles.Administrator)) throw new ForbiddenException();
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var actions = await db.AuditEntries.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId)
            .Select(x => x.Action)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);
        var entityTypes = await db.AuditEntries.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId)
            .Select(x => x.EntityType)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);
        return new(actions, entityTypes);
    }

    public async Task<PageResult<AuditListItem>> SearchAsync(PageRequest request, string? action = null, string? entityType = null, DateOnly? from = null, DateOnly? through = null, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.GetAsync(cancellationToken);
        if (!user.IsInRole(PortalRoles.Administrator)) throw new ForbiddenException();
        await using var db = await Factory.CreateDbContextAsync(cancellationToken);
        var query = from audit in db.AuditEntries.AsNoTracking()
                    join actor in db.Users.AsNoTracking() on audit.UserId equals actor.Id
                    where audit.OrganizationId == user.OrganizationId
                    select new { audit, actor };
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(x => x.audit.Action == action);
        if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(x => x.audit.EntityType == entityType);
        if (from.HasValue) { var start = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); query = query.Where(x => x.audit.OccurredAtUtc >= start); }
        if (through.HasValue) { var end = through.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); query = query.Where(x => x.audit.OccurredAtUtc < end); }
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.audit.OccurredAtUtc).Skip((request.SafePage - 1) * request.SafePageSize).Take(request.SafePageSize)
            .Select(x => new AuditListItem(
                x.audit.Id,
                x.actor.DisplayName,
                x.audit.Action,
                x.audit.EntityType,
                x.audit.EntityId,
                x.audit.Summary,
                x.audit.OccurredAtUtc,
                x.actor.Id,
                x.actor.AvatarImage == null ? null : "/avatars/" + x.actor.Id))
            .ToListAsync(cancellationToken);
        return new(items, count, request.SafePage, request.SafePageSize);
    }
}

internal static class ExcelReportWriter
{
    public static byte[] Create(IReadOnlyList<ReportRow> rows, ReportFilter filter, string organizationName)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var styles = workbookPart.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = CreateStyles();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(
                new SheetViews(new SheetView(new Pane { VerticalSplit = 4, TopLeftCell = "A5", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen }) { WorkbookViewId = 0 }),
                new Columns(
                    new Column { Min = 1, Max = 1, Width = 13, CustomWidth = true },
                    new Column { Min = 2, Max = 4, Width = 24, CustomWidth = true },
                    new Column { Min = 5, Max = 5, Width = 12, CustomWidth = true },
                    new Column { Min = 6, Max = 6, Width = 48, CustomWidth = true },
                    new Column { Min = 7, Max = 7, Width = 14, CustomWidth = true }),
                sheetData);
            sheetData.Append(TextRow(1, [$"{organizationName} · Time report"], 1));
            sheetData.Append(TextRow(2, [$"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"], 0));
            sheetData.Append(TextRow(3, [$"Filters: {filter.From:yyyy-MM-dd} to {filter.To:yyyy-MM-dd}"], 0));
            sheetData.Append(TextRow(4, ["Date", "Client", "Project", "Employee", "Hours", "Description", "Status"], 1));
            uint index = 5;
            foreach (var row in rows)
            {
                var excelDate = row.WorkDate.ToDateTime(TimeOnly.MinValue).ToOADate();
                sheetData.Append(new Row(
                    NumberCell(excelDate, 2),
                    TextCell(row.ClientName),
                    TextCell(row.ProjectName),
                    TextCell(row.UserName),
                    NumberCell((double)row.Hours, 3),
                    TextCell(row.Description),
                    TextCell(row.Status.ToString()))
                { RowIndex = index++ });
            }
            worksheetPart.Worksheet.Append(new AutoFilter { Reference = $"A4:G{Math.Max(4, index - 1)}" });
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1, Name = "Time report" });
            workbookPart.Workbook.Save();
        }
        return stream.ToArray();
    }

    private static Row TextRow(uint index, IReadOnlyList<string> values, uint style) =>
        new(values.Select(value => TextCell(value, style))) { RowIndex = index };

    private static Cell TextCell(string? value, uint style = 0)
    {
        var safe = Sanitize(value ?? "");
        if (safe.StartsWith('=') || safe.StartsWith('+') || safe.StartsWith('-') || safe.StartsWith('@')) safe = "'" + safe;
        return new Cell { DataType = CellValues.InlineString, StyleIndex = style, InlineString = new InlineString(new Text(safe)) };
    }

    private static Cell NumberCell(double value, uint style) => new() { CellValue = new CellValue(value), DataType = CellValues.Number, StyleIndex = style };

    private static string Sanitize(string value) => new(value.Where(ch => ch is '\t' or '\n' or '\r' || ch >= ' ').ToArray());

    private static Stylesheet CreateStyles() => new(
        new Fonts(new Font(), new Font(new Bold())) { Count = 2 },
        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
        new Borders(new Border()) { Count = 1 },
        new CellFormats(
            new CellFormat(),
            new CellFormat { FontId = 1, ApplyFont = true },
            new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = 2, ApplyNumberFormat = true })
        { Count = 4 });
}
