namespace SmartX.Shared.Models;

public enum SensorCategory
{
    Environmental,   // e.g. soil moisture, temperature - float payloads
    PowerConsumption, // e.g. wattage - int payloads
    Actuator          // e.g. valve/switch state - bool payloads
}
