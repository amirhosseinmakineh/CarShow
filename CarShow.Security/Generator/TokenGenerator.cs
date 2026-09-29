using CarShow.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
namespace CarShow.Security.Token
{
    public class TokenGenerator : ITokenGenerator
    {
        private readonly IConfiguration configuration;

        public TokenGenerator(IConfiguration configuration)
        {
            this.configuration = configuration;
        }
        public string GenerateToken(User user, List<Role> roles)
        {
            var jwtSetting = configuration.GetSection("JwtSettings");
            var expiryMinutes = Convert.ToDouble(jwtSetting["ExpiryMinutes"]);
            var secretKey = Encoding.UTF8.GetBytes(jwtSetting["SecretKey"]);
            var audience = jwtSetting["Audience"];
            var issuer = jwtSetting["Issuer"];

            var claims = new List<Claim>()
            {
                new Claim("MobileNumber", user.MobileNumber),
                new Claim("Id", user.Id.ToString()),
                new Claim("firstName",user.Name),
                new Claim("lastName",user.Family)
            };

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role.RoleName));
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Audience = audience,
                Issuer = issuer,
                Expires = DateTime.UtcNow.AddMinutes(expiryMinutes),

                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(secretKey),
                    SecurityAlgorithms.HmacSha256
                )
            };

            var tokenHandler = new JwtSecurityTokenHandler();

            var token = tokenHandler.CreateToken(tokenDescriptor);

            return tokenHandler.WriteToken(token);
        }

        public ClaimsPrincipal ValidateToken(string token)
        {
            var jwtSetting = configuration.GetSection("JwtSettings");

            var secretKey = Encoding.UTF8.GetBytes(jwtSetting["SecretKey"]);
            var issuer = jwtSetting["Issuer"];
            var audience = jwtSetting["Audience"];

            var parameters = new TokenValidationParameters()
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,

                ValidIssuer = issuer,
                ValidAudience = audience,

                IssuerSigningKey = new SymmetricSecurityKey(secretKey)
            };

            var tokenHandler = new JwtSecurityTokenHandler();

            var principal = tokenHandler.ValidateToken(
                token,
                parameters,
                out SecurityToken validatedToken
            );

            return principal;
        }

    }
}
