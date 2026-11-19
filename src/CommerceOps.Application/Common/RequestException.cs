namespace CommerceOps.Application.Common;

public enum RequestError
{
    Validation,
    NotFound,
    Conflict
}

public sealed class RequestException : Exception
{
    private RequestException(RequestError error, string message, Dictionary<string, string[]>? errors = null)
        : base(message)
    {
        Error = error;
        Errors = errors;
    }

    public RequestError Error { get; }
    public Dictionary<string, string[]>? Errors { get; }

    public static RequestException Invalid(Dictionary<string, string[]> errors) =>
        new(RequestError.Validation, "One or more validation errors occurred.", errors);

    public static RequestException NotFound(string resource) =>
        new(RequestError.NotFound, $"The {resource} does not exist.");

    public static RequestException Conflict(string message) => new(RequestError.Conflict, message);
}
