namespace TToApp.Services.Auth
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.IdentityModel.Tokens;
    using System.IdentityModel.Tokens.Jwt;
    using System.Security.Claims;
    using System.Text;
    using TToApp.Model;

    public class JwtService : IJwtService
    {
        private readonly IConfiguration _config;

        public JwtService(IConfiguration config)
        {
            _config = config;
        }

        public string CreateJwtToken(User user, Company? company)
        {
            var key = Encoding.ASCII.GetBytes(_config["JwtSettings:Secret"]);

            var companyLogo = company?.LogoUrl
                              ?? user.Warehouse?.Companie?.LogoUrl
                              ?? "";

            var companyId = user.CompanyId?.ToString()
                       ?? company?.Id.ToString()
                       ?? user.Warehouse?.CompanyId.ToString()
                       ?? "0";
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, $"{user.Name} {user.LastName}"),
                new Claim(ClaimTypes.Role, user.UserRole?.ToString() ?? ""),
                new Claim(ClaimTypes.Email, user.Email ?? ""),
                new Claim("avatar_url", user.AvatarUrl ?? ""),
                new Claim("companyId", user.CompanyId.ToString() ?? ""),
                new Claim("WarehouseID", user.WarehouseId?.ToString() ?? "0"),
                new Claim("CompanyLogo", companyLogo),
                new Claim("CompanyId", companyId)
            };

            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddDays(1),
                SigningCredentials = credentials,
                Issuer = _config["JwtSettings:Issuer"],
                Audience = _config["JwtSettings:Audience"]
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            return tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));
        }
    }
}
