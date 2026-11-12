using CineMatch.Core;
using CineMatch.Core.Interfaces;
using CineMatch.Core.Models;
using CineMatch.Infrastructure.Mongo;
using MongoDB.Driver;

namespace CineMatch.Infrastructure.Repositories;

public class UserRepository(MongoContext ctx) : IUserRepository
{
    public async Task<User?> GetByIdAsync(int id, CancellationToken ct = default) =>
        await ctx.Users.Find(u => u.Id == id).FirstOrDefaultAsync(ct);

    public async Task<User?> GetByLoginAsync(string usernameOrEmail, CancellationToken ct = default)
    {
        var login = usernameOrEmail.Trim().ToLowerInvariant();
        return await ctx.Users.Find(u => u.Username == login || u.Email == login).FirstOrDefaultAsync(ct);
    }

    public async Task<User> CreateAsync(User user, CancellationToken ct = default)
    {
        user.Username = user.Username.Trim().ToLowerInvariant();
        user.Email = user.Email.Trim().ToLowerInvariant();
        user.Id = await ctx.NextIdAsync(MongoContext.UsersName, ct);
        try
        {
            await ctx.Users.InsertOneAsync(user, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var field = ex.WriteError.Message.Contains("email", StringComparison.OrdinalIgnoreCase) ? "email" : "username";
            throw new ConflictException($"This {field} is already taken.");
        }
        return user;
    }

    public async Task<bool> UpdateProfileAsync(int id, UserProfile profile, CancellationToken ct = default)
    {
        var result = await ctx.Users.UpdateOneAsync(
            u => u.Id == id,
            Builders<User>.Update.Set(u => u.Profile, profile),
            cancellationToken: ct);
        return result.MatchedCount == 1;
    }
}
