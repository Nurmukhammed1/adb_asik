using System.Net;
using System.Net.Http.Json;
using CineMatch.Api.Contracts;

namespace CineMatch.Tests;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFixture api)
{
    [Fact]
    public async Task Register_returns_a_token_that_opens_the_profile()
    {
        var (client, user) = await api.NewUserAsync();

        var me = await client.GetAsync<UserDto>("/api/users/me");

        Assert.Equal(user.Id, me.Id);
        Assert.Equal(user.Username, me.Username);
        Assert.Equal("user", me.Role);
        Assert.Equal(user.Username, me.Profile.DisplayName);
    }

    [Fact]
    public async Task Register_response_never_contains_the_password_hash()
    {
        var name = ApiFixture.Unique("user");
        var response = await api.Anonymous().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { Username = name, Email = $"{name}@example.com", Password = ApiFixture.Password });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.DoesNotContain("hash", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_works_with_username_or_email_in_any_letter_case()
    {
        var (_, user) = await api.NewUserAsync();
        var client = api.Anonymous();

        foreach (var login in new[] { user.Username, user.Username.ToUpperInvariant(), user.Email.ToUpperInvariant() })
        {
            var response = await client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest { Username = login, Password = ApiFixture.Password });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(user.Id, (await response.ReadAsync<AuthResponse>()).User.Id);
        }
    }

    [Fact]
    public async Task Login_with_a_wrong_password_or_unknown_user_is_rejected()
    {
        var (_, user) = await api.NewUserAsync();
        var client = api.Anonymous();

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Username = user.Username, Password = "not-the-password" });
        var unknownUser = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Username = ApiFixture.Unique("nobody"), Password = ApiFixture.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
    }

    [Fact]
    public async Task Duplicate_username_or_email_is_a_conflict()
    {
        var (_, user) = await api.NewUserAsync();
        var client = api.Anonymous();

        var sameUsername = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = user.Username.ToUpperInvariant(),
            Email = $"{ApiFixture.Unique("other")}@example.com",
            Password = ApiFixture.Password,
        });
        var sameEmail = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = ApiFixture.Unique("other"),
            Email = user.Email.ToUpperInvariant(),
            Password = ApiFixture.Password,
        });

        Assert.Equal(HttpStatusCode.Conflict, sameUsername.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, sameEmail.StatusCode);
    }

    [Fact]
    public async Task Invalid_registration_lists_every_bad_field()
    {
        var response = await api.Anonymous().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { Username = "a", Email = "not-an-email", Password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = await response.ReadErrorFieldsAsync();
        Assert.Contains("Username", fields);
        Assert.Contains("Email", fields);
        Assert.Contains("Password", fields);
    }

    [Fact]
    public async Task Protected_endpoints_reject_missing_and_forged_tokens()
    {
        var anonymous = await api.Anonymous().GetAsync("/api/users/me");
        var forged = await api.Anonymous().WithToken("not.a.token").GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
    }

    [Fact]
    public async Task Profile_update_is_saved_and_removes_duplicate_genres()
    {
        var (client, _) = await api.NewUserAsync();

        var response = await client.PutAsJsonAsync("/api/users/me", new UpdateProfileRequest
        {
            DisplayName = "  Movie Fan  ",
            FavoriteGenres = ["Sci-Fi", "Comedy", "comedy", " "],
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await client.GetAsync<UserDto>("/api/users/me");
        Assert.Equal("Movie Fan", me.Profile.DisplayName);
        Assert.Equal(["Sci-Fi", "Comedy"], me.Profile.FavoriteGenres);
    }
}
