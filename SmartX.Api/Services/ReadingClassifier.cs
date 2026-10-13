using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public static class ReadingClassifier
{
    public const double MoistureFloor = 20, MoistureCeiling = 70, PowerCeiling = 4000, PowerSpikeDelta = 2000;
    public static readonly string[] ReadingAlertTypes = { "MOISTURE_CRITICAL", "OUT_OF_RANGE", "POWER_SPIKE" };

    public static (AlertSeverity? Severity, string? AlertType, string? Message) Classify(
        SensorCategory category, double value, double? previous)
    {
        switch (category)
        {
            case SensorCategory.Environmental:
                if (value < MoistureFloor)
                    return (AlertSeverity.Critical, "MOISTURE_CRITICAL", $"Moisture critically low at {value:F1}% (floor {MoistureFloor}%).");
                if (value > MoistureCeiling)
                    return (AlertSeverity.Warning, "OUT_OF_RANGE", $"Reading {value:F1} above safe ceiling {MoistureCeiling}.");
                break;

            case SensorCategory.PowerConsumption:
                if (value > PowerCeiling)
                    return (AlertSeverity.Critical, "POWER_SPIKE", $"Power spike: {value:F0} W (limit {PowerCeiling} W).");
                if (previous is not null)
                {
                    
                    var delta = new PowerReading("now", value) - new PowerReading("prev", previous.Value);
                    if (delta > new PowerReading("limit", PowerSpikeDelta))
                        return (AlertSeverity.Critical, "POWER_SPIKE", $"Load jumped {delta.Watts:F0} W in one reading.");
                }
                if (value < 0)
                    return (AlertSeverity.Warning, "OUT_OF_RANGE", $"Negative power reading {value:F0} W.");
                break;
        }
        return (null, null, null);
    }
}