namespace TalkToRag.Application.Common.Exceptions;

/// <summary>Bad input from the caller (HTTP 400).</summary>
public sealed class RequestValidationException(string message) : Exception(message);

/// <summary>The request clashes with existing data, such as a taken email (HTTP 409).</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>Wrong email or password (HTTP 401). Deliberately does not say which.</summary>
public sealed class AuthenticationFailedException() : Exception("Email or password is incorrect.");

/// <summary>The thing asked for does not exist, or is not the caller's (HTTP 404).</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>The AI service rejected the document, such as a scanned PDF with no text layer. The ingestion worker stores the message as the source's failure reason.</summary>
public sealed class AiIngestionException(string message) : Exception(message);

/// <summary>The AI service or underlying model server is unavailable (HTTP 503).</summary>
public sealed class AiServiceUnavailableException(string message) : Exception(message);
