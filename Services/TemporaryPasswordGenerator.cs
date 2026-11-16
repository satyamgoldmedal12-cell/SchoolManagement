using System.Security.Cryptography;
using SchoolManagement.Interfaces.ServiceInterfaces;

namespace SchoolManagement.Services;

public sealed class TemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    public string Generate()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(12);
        var randomText = Convert.ToHexString(randomBytes).ToLowerInvariant();
        return "Sm9!" + randomText;
    }
}
