namespace TToApp.Services.Auth
{
    using TToApp.Model;

    public interface IJwtService
    {
        string CreateJwtToken(User user, Company? company);
    }
}
