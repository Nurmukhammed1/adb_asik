namespace CineMatch.Core.Models;

public static class Roles
{
    public const string User = "user";
    public const string Admin = "admin";
}

/// <summary>A platform user. Stored in the <c>users</c> collection.</summary>
public class User
{
    public int Id { get; set; }

    /// <summary>Lower-cased, unique.</summary>
    public string Username { get; set; } = "";

    /// <summary>Lower-cased, unique.</summary>
    public string Email { get; set; } = "";

    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = Roles.User;
    public UserProfile Profile { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class UserProfile
{
    public string DisplayName { get; set; } = "";
    public List<string> FavoriteGenres { get; set; } = [];
}
