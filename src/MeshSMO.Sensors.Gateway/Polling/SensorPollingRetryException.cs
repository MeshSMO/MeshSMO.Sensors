namespace MeshSMO.Sensors.Gateway.Polling;

internal sealed class SensorPollingRetryException : Exception
{
    public SensorPollingRetryException()
    {
    }

    public SensorPollingRetryException(string message) : base(message)
    {
    }

    public SensorPollingRetryException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
