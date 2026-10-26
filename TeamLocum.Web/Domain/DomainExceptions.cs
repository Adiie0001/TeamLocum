namespace TeamLocum.Web.Domain;

/// <summary>A rule of the domain was broken. The message is safe to show to the user.</summary>
public class DomainException(string message) : Exception(message);

public sealed class InvalidTransitionException(string message) : DomainException(message);

/// <summary>The request is valid but conflicts with current data (for example an overlapping booking).</summary>
public sealed class ConflictException(string message) : DomainException(message);

/// <summary>The record does not exist or the caller may not know that it exists.</summary>
public sealed class NotFoundException(string message) : DomainException(message);

public sealed class ForbiddenException(string message) : DomainException(message);
