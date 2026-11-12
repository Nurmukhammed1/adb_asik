namespace CineMatch.Core;

/// <summary>A write was rejected because it would duplicate a unique value. The API maps it to HTTP 409.</summary>
public class ConflictException(string message) : Exception(message);
