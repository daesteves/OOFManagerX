namespace OOFManagerX.Core.Models;

/// <summary>
/// Represents a saved OOF message template.
/// </summary>
public record Template(
    string Id,
    string Name,
    string InternalMessage,
    string ExternalMessage,
    DateTime CreatedAt
)
{
    public static Template Create(string name, string internalMessage, string externalMessage)
    {
        return new Template(
            Guid.NewGuid().ToString(),
            name,
            internalMessage,
            externalMessage,
            DateTime.UtcNow
        );
    }
}
