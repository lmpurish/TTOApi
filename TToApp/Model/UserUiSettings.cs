
namespace TToApp.Model
{
    public class UserUiSettings
    {
        public int Id { get; set; }
        public int UserId { get; set; }

        public string Theme { get; set; } = "light";
        public string ActiveTheme { get; set; } = "blue_theme";
        public bool Horizontal { get; set; } = false;
        public bool CardBorder { get; set; } = false;
        public bool Boxed { get; set; } = false;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public User User { get; set; } = default!;
    }
}
