namespace BusinessPortal.Domain;

public enum ClientStatus { Active, Inactive }
public enum ProjectStatus { Planned, Active, OnHold, Completed, Archived }
public enum WorkItemStatus { Open, InProgress, Blocked, Done }
public enum WorkItemPriority { Low, Normal, High, Critical }
public enum TimeEntryStatus { Draft, Submitted, Approved, Returned }
public enum TimeEntryActivityType { Created, Updated, Submitted, Approved, Returned, Reopened, Comment }
public enum WorkItemActivityType { Created, Updated, Assigned, Returned, Completed, Comment }
public enum NotificationType { TimeEntrySubmitted, TimeEntryApproved, TimeEntryReturned, TimeEntryCommented, ProjectCompleted, WorkItemAssigned, WorkItemReturned, WorkItemCommented }
