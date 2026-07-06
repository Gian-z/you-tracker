namespace YouTracker.Infrastructure.YouTrack;

/// <summary>Thrown when the YouTrack REST API returns a non-success status code.</summary>
public sealed class YouTrackApiException : Exception
{
    public int StatusCode { get; }
    public string ResponseBody { get; }
    public string RequestUrl { get; }

    public YouTrackApiException(int statusCode, string responseBody, string requestUrl)
        : base(
            $"YouTrack API request '{requestUrl}' failed with status {statusCode}: {responseBody}"
        )
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        RequestUrl = requestUrl;
    }
}
