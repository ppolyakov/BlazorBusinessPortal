namespace BusinessPortal.Domain;

public enum ClientStatus { Active, Inactive }
public enum ProjectStatus { Planned, Active, OnHold, Completed, Archived }
public enum WorkItemStatus { Open, InProgress, Blocked, Done }
public enum WorkItemPriority { Low, Normal, High, Critical }
public enum TimeEntryStatus { Draft, Submitted, Approved, Rejected }
public enum NotificationType { TimeEntrySubmitted, TimeEntryApproved, TimeEntryRejected, ProjectCompleted, WorkItemAssigned }
