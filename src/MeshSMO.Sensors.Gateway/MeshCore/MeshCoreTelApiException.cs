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

    public HttpStatusCode StatusCode { get; }

    public string ResponseBody { get; }
}
