using System.Net;

namespace MeshSMO.Sensors.Gateway.MeshCore;

public sealed class MeshCoreTelApiException : Exception
{
    public MeshCoreTelApiException(HttpStatusCode statusCode, string responseBody)
        : base($"MeshCoreTel API returned HTTP {(int)statusCode} ({statusCode}): {responseBody}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public MeshCoreTelApiException() : base()
    {
    }

    public MeshCoreTelApiException(string? message) : base(message)
    {
    }

    public MeshCoreTelApiException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    public HttpStatusCode StatusCode { get; }

    public string? ResponseBody { get; }
}
