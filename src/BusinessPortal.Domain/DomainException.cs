namespace BusinessPortal.Domain;

public sealed class DomainException(string message) : InvalidOperationException(message);
