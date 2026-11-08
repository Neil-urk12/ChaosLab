namespace Backend.Domain.Enums;

// Production itself is never a supported target; ProductionMirror is a production-like copy.
public enum EnvironmentKind
{
    Development,
    Test,
    Staging,
    ProductionMirror
}
