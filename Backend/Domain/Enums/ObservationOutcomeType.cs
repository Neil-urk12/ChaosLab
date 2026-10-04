
namespace Backend.Domain.Enums;

public enum ObservationOutcomeType
{
    Forwarded,
    SyntheticResponse,
    ModifiedResponse,
    Dropped,
    Timeout,
    ConnectionError,
    ProxyError
}
