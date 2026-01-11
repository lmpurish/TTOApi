namespace TToApp.DTOs
{
    public class UserUiSettingsDTO
    {
        public string Theme { get; set; } = "light";
        public string ActiveTheme { get; set; } = "blue_theme";
        public string Dir { get; set; } = "ltr";
        public bool SidenavCollapsed { get; set; } = false;
        public bool Horizontal { get; set; } = false;
        public bool CardBorder { get; set; } = false;
        public bool Boxed { get; set; } = false;
    }
}
