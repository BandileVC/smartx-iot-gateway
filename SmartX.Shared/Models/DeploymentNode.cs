namespace SmartX.Shared.Models;

/// <summary>
/// Represents one node in a multi-tier deployment hierarchy, e.g.
/// Facility A -> Zone 1 -> Sub-Zone B -> Node (sensor).
/// A node may contain further child nodes (nested zones) and/or be a leaf
/// that hosts an actual sensor (HostsSensor = true).
/// </summary>
public class DeploymentNode
{
    public string Name { get; set; } = string.Empty;
    public bool HostsSensor { get; set; }
    public List<DeploymentNode> Children { get; set; } = new();

    /// <summary>
    /// Recursively validates that a node with the given path (e.g.
    /// ["Facility A", "Zone 1", "Sub-Zone B"]) exists and is safely configured:
    /// every ancestor must exist, be uniquely named among its siblings, and only
    /// leaf nodes are allowed to directly host a sensor.
    /// Base case: path has exactly one segment left -> check it exists at this level.
    /// Recursive case: descend into the matching child and validate the remaining path.
    /// </summary>
    public static ValidationResult ValidatePath(DeploymentNode root, IReadOnlyList<string> path, int depth = 0)
    {
        if (path.Count == 0)
            return ValidationResult.Fail("Empty deployment path.");

        var currentSegment = path[0];
        var match = root.Children.FirstOrDefault(c =>
            string.Equals(c.Name, currentSegment, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return ValidationResult.Fail($"'{currentSegment}' not found under '{root.Name}' (depth {depth}).");

        // Guard against pathological/malicious deeply-nested trees.
        if (depth > 32)
            return ValidationResult.Fail("Deployment hierarchy exceeds maximum safe depth (32).");

        // Base case: this was the last segment in the path.
        if (path.Count == 1)
        {
            return match.Children.Count > 0 && match.HostsSensor
                ? ValidationResult.Fail($"'{match.Name}' both hosts a sensor and has sub-zones — invalid.")
                : ValidationResult.Ok($"Path resolved safely at '{match.Name}' (depth {depth + 1}).");
        }

        // Recursive case: descend one level and validate the remaining path.
        return ValidatePath(match, path.Skip(1).ToList(), depth + 1);
    }

    /// <summary>Recursively counts every node (zones + leaf sensors) beneath this node, inclusive.</summary>
    public int CountAllNodes() => 1 + Children.Sum(c => c.CountAllNodes());
}

public record ValidationResult(bool IsValid, string Message)
{
    public static ValidationResult Ok(string message) => new(true, message);
    public static ValidationResult Fail(string message) => new(false, message);
}
