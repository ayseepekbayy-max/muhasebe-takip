using Microsoft.AspNetCore.Identity;

namespace MuhasebeTakip2.App.Services;

public sealed class PrivateAccessService(IConfiguration configuration)
{
    private readonly PasswordHasher<PrivatePerson> _hasher = new();

    public PrivateAccessResult Authenticate(string? username, string? password)
    {
        var configuredUsername = configuration["FIRMOVA_PRIVATE_USERNAME"]?.Trim();
        if (string.IsNullOrWhiteSpace(configuredUsername) ||
            !string.Equals(username?.Trim(), configuredUsername, StringComparison.OrdinalIgnoreCase))
            return new(false, null);

        // A configured name is reserved even if the remaining configuration is incomplete.
        var hash1 = configuration["FIRMOVA_PRIVATE_PASSWORD_HASH_1"];
        var hash2 = configuration["FIRMOVA_PRIVATE_PASSWORD_HASH_2"];
        var name1 = configuration["FIRMOVA_PRIVATE_PERSON_1"];
        var name2 = configuration["FIRMOVA_PRIVATE_PERSON_2"];
        if (string.IsNullOrWhiteSpace(hash1) || string.IsNullOrWhiteSpace(hash2) ||
            string.IsNullOrWhiteSpace(name1) || string.IsNullOrWhiteSpace(name2) ||
            string.IsNullOrEmpty(password))
            return new(true, null);

        var person1 = new PrivatePerson("1", name1);
        var person2 = new PrivatePerson("2", name2);
        var matches1 = Verify(person1, hash1, password);
        var matches2 = Verify(person2, hash2, password);

        // Ambiguous credentials must never select the wrong person.
        return new(true, matches1 == matches2 ? null : matches1 ? person1 : person2);
    }

    public string GetOtherPersonName(string personNumber) =>
        configuration[personNumber == "1" ? "FIRMOVA_PRIVATE_PERSON_2" : "FIRMOVA_PRIVATE_PERSON_1"] ?? "";

    private bool Verify(PrivatePerson person, string hash, string password)
    {
        try
        {
            return _hasher.VerifyHashedPassword(person, hash, password) is
                PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException or System.Security.Cryptography.CryptographicException)
        {
            // Malformed configuration fails closed; never expose credentials in logs/errors.
            return false;
        }
    }
}

public sealed record PrivatePerson(string Number, string Name);
public sealed record PrivateAccessResult(bool IsPrivateUsername, PrivatePerson? Person);