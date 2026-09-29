using CarShow.Domain.Models;
using System.Security.Claims;
namespace CarShow.Security.Token
{
    public interface ITokenGenerator
    {
        string GenerateToken(User user, List<Role> roles);
        ClaimsPrincipal ValidateToken(string token);
    }
}
