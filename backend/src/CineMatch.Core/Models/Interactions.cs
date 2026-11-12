namespace CineMatch.Core.Models;

/// <summary>One user's rating of one movie. Stored in the <c>ratings</c> collection.</summary>
public class Rating
{
    public int UserId { get; set; }
    public int MovieId { get; set; }

    /// <summary>0.5 to 5 in half-star steps. Stored as the <c>rating</c> field.</summary>
    public double Value { get; set; }

    public DateTime Ts { get; set; }
}

public static class EventTypes
{
    public const string View = "view";
}

/// <summary>An append-only interaction event. Stored in the <c>events</c> time-series collection.</summary>
public class InteractionEvent
{
    public DateTime Ts { get; set; }
    public EventMeta Meta { get; set; } = new();
    public int MovieId { get; set; }
}

public class EventMeta
{
    public int UserId { get; set; }
    public string Type { get; set; } = EventTypes.View;
}

/// <summary>A movie a user saved for later. Stored in the <c>watchlist</c> collection.</summary>
public class WatchlistItem
{
    public int UserId { get; set; }
    public int MovieId { get; set; }
    public DateTime AddedAt { get; set; }
}
