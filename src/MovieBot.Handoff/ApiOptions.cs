namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// Where the API is, for what the hand-off asks it: which films the rooms hold, and which settled
/// films the library's clock says are due.
/// </summary>
public sealed class ApiOptions
{
    public const string Section = "Api";

    public string BaseUrl { get; set; } = "http://127.0.0.1:8099";

    /// <summary>
    /// The key a service proves itself with. The rooms are behind the same door as everything
    /// else the API serves, and this process has no Discord user to be.
    /// </summary>
    public string ServiceKey { get; set; } = "";
}
