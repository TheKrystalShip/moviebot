using Microsoft.AspNetCore.SignalR;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>Marks a hub method a remote may call. Every other method refuses a remote.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RemoteAllowedAttribute : Attribute;

/// <summary>
/// Refuses a remote any hub method not marked <see cref="RemoteAllowedAttribute"/>. A remote moves the
/// film for its screen's room and is in nothing else: it is no participant, plays nothing, and answers
/// no door, so a method added to the hub is closed to it until somebody opens it on purpose.
/// </summary>
public sealed class RemoteGate : IHubFilter
{
    /// <summary>The <c>Context.Items</c> key that marks a connection as a remote, set by <c>JoinAsRemote</c>.</summary>
    public const string ItemKey = "remote";

    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocation, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (invocation.Context.Items.ContainsKey(ItemKey)
            && !invocation.HubMethod.IsDefined(typeof(RemoteAllowedAttribute), inherit: true))
            throw new HubException("A remote moves the film; it is not in the room.");
        return next(invocation);
    }
}
