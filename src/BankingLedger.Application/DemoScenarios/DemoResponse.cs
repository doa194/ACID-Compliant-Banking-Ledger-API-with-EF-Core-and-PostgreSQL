namespace BankingLedger.Application.DemoScenarios;

/// Structured response that explains what an ACID demo endpoint just demonstrated.
/// Each field is chosen to make the ACID principle visible and verifiable.
public record DemoResponse(
    string AcidPrinciple,
    string Scenario,
    string Result,
    string Explanation,
    Dictionary<string, object> Details
);
