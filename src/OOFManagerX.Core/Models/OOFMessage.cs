namespace OOFManagerX.Core.Models;

/// <summary>
/// Represents an Out-of-Office message with internal and external variants.
/// </summary>
public record OOFMessage
{
    /// <summary>
    /// Message shown to internal (same organization) recipients.
    /// </summary>
    public string InternalMessage { get; init; } = string.Empty;

    /// <summary>
    /// Message shown to external recipients.
    /// </summary>
    public string ExternalMessage { get; init; } = string.Empty;

    public OOFMessage() { }

    public OOFMessage(string internalMessage, string externalMessage)
    {
        InternalMessage = internalMessage;
        ExternalMessage = externalMessage;
    }
}
