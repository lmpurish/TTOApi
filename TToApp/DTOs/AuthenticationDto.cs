namespace TToApp.DTOs
{
    public class AuthenticationDto
    {
        public string Login { get; set; } = null!;   // email o número de teléfono
        public string Password { get; set; } = null!;
    }
}
