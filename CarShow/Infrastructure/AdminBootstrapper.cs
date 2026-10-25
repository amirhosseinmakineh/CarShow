using System.Security.Cryptography;
using Carshow.Utilities.Hasher;
using CarShow.Domain.Models;
using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore;

namespace CarShow.Infrastructure;

/// <summary>
/// Creates the initial administrator only when explicitly enabled through
/// deployment environment variables. It is idempotent and never changes an
/// existing user's password.
/// </summary>
public static class AdminBootstrapper
{
    private const string AdminRoleName = "Admin";
    private const string CustomerRoleName = "Customer";

    public static async Task EnsureAsync(
        CarShowContext db,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue("AdminBootstrap:Enabled", false))
            return;

        var mobileNumber = configuration["AdminBootstrap:MobileNumber"]?.Trim();
        if (string.IsNullOrWhiteSpace(mobileNumber))
            throw new InvalidOperationException(
                "AdminBootstrap:MobileNumber must be set when AdminBootstrap is enabled.");

        var adminRole = await GetOrCreateRoleAsync(db, AdminRoleName, cancellationToken);
        await GetOrCreateRoleAsync(db, CustomerRoleName, cancellationToken);

        var user = await db.Users.FirstOrDefaultAsync(
            x => x.MobileNumber == mobileNumber,
            cancellationToken);

        string? generatedPassword = null;
        if (user is null)
        {
            generatedPassword = configuration["AdminBootstrap:Password"];
            if (string.IsNullOrWhiteSpace(generatedPassword))
                generatedPassword = GenerateTemporaryPassword();

            user = new User
            {
                Id = Guid.NewGuid(),
                MobileNumber = mobileNumber,
                Name = configuration["AdminBootstrap:Name"]?.Trim() ?? "CarShow",
                Family = configuration["AdminBootstrap:Family"]?.Trim() ?? "Administrator",
                Email = configuration["AdminBootstrap:Email"]?.Trim(),
                Password = PasswordHasher.HashPassword(generatedPassword),
                IsDelete = false
            };

            await db.Users.AddAsync(user, cancellationToken);
        }

        var hasAdminRole = await db.UserRoles.AnyAsync(
            x => x.UserId == user.Id && x.RoleId == adminRole.Id,
            cancellationToken);

        if (!hasAdminRole)
        {
            await db.UserRoles.AddAsync(new UserRole
            {
                UserId = user.Id,
                RoleId = adminRole.Id,
                IsDelete = false
            }, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (generatedPassword is not null)
        {
            logger.LogWarning(
                "Initial CarShow administrator created for mobile {MobileNumber}. " +
                "Temporary password (retrieve once, then disable AdminBootstrap): {TemporaryPassword}",
                mobileNumber,
                generatedPassword);
        }
        else
        {
            logger.LogInformation(
                "CarShow administrator bootstrap verified for mobile {MobileNumber}.",
                mobileNumber);
        }
    }

    private static async Task<Role> GetOrCreateRoleAsync(
        CarShowContext db,
        string roleName,
        CancellationToken cancellationToken)
    {
        var role = await db.Roles.FirstOrDefaultAsync(
            x => x.RoleName == roleName,
            cancellationToken);

        if (role is not null)
            return role;

        role = new Role { RoleName = roleName, IsDelete = false };
        await db.Roles.AddAsync(role, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return role;
    }

    private static string GenerateTemporaryPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
        Span<char> password = stackalloc char[18];
        for (var i = 0; i < password.Length; i++)
            password[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

        return new string(password);
    }
}
