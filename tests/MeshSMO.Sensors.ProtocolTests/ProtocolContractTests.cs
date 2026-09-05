using MeshSMO.Sensors.Application.Protocol;

namespace MeshSMO.Sensors.ProtocolTests;

public sealed class ProtocolContractTests
{
    [Fact]
    public void Poll_request_id_preserves_full_unsigned_32_bit_range()
    {
        var requestId = new PollRequestId(uint.MaxValue);

        Assert.Equal(uint.MaxValue, requestId.Value);
    }
}
