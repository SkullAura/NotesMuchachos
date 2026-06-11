using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ProjectCal.Api.Data.Entities;

namespace ProjectCal.Api.Services;

public static class PasswordService
{
    private const int Iterations = 100_000;

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }
}

public sealed class TokenService(IConfiguration configuration)
{
    public (string AccessToken, DateTimeOffset ExpiresAt) CreateAccessToken(UserEntity user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(configuration.GetValue("Jwt:AccessTokenMinutes", 30));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(GetSigningKey(configuration)));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("email_confirmed", user.EmailConfirmed.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"] ?? "ProjectCal",
            audience: configuration["Jwt:Audience"] ?? "ProjectCal.Client",
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public static string GetSigningKey(IConfiguration configuration)
    {
        var key = configuration["Jwt:SigningKey"];
        return string.IsNullOrWhiteSpace(key)
            ? "dev-only-change-this-signing-key-32-bytes"
            : key;
    }
}

public interface IFileStorage
{
    Task<string> SaveAsync(Guid userId, Guid attachmentId, IFormFile file, CancellationToken cancellationToken);
    Task<(Stream Stream, string FileName, string MimeType)?> OpenAsync(AttachmentEntity attachment, CancellationToken cancellationToken);
}

public sealed class LocalFileStorage(IWebHostEnvironment environment, IConfiguration configuration) : IFileStorage
{
    public async Task<string> SaveAsync(Guid userId, Guid attachmentId, IFormFile file, CancellationToken cancellationToken)
    {
        var root = configuration["Storage:RootPath"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(environment.ContentRootPath, "App_Data", "media");
        }

        var extension = Path.GetExtension(file.FileName);
        var relativePath = Path.Combine(userId.ToString("N"), $"{attachmentId:N}{extension}");
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var output = File.Create(fullPath);
        await file.CopyToAsync(output, cancellationToken);
        return relativePath.Replace('\\', '/');
    }

    public Task<(Stream Stream, string FileName, string MimeType)?> OpenAsync(AttachmentEntity attachment, CancellationToken cancellationToken)
    {
        var root = configuration["Storage:RootPath"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(environment.ContentRootPath, "App_Data", "media");
        }

        var fullPath = Path.Combine(root, attachment.StoredPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return Task.FromResult<(Stream, string, string)?>(null);
        }

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult<(Stream, string, string)?>((stream, attachment.FileName, attachment.MimeType));
    }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : throw new UnauthorizedAccessException("Missing user id.");
    }
}
