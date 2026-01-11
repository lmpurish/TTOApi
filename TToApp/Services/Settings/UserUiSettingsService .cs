using Microsoft.EntityFrameworkCore;
using TToApp.DTOs;
using TToApp.Model;

namespace TToApp.Services.Settings
{
    public interface IUserUiSettingsService
    {
        Task<UserUiSettingsDTO> GetForUserAsync(int userId);
        Task<UserUiSettingsDTO> UpsertAsync(int userId, UserUiSettingsDTO dto);
    }

    public class UserUiSettingsService : IUserUiSettingsService
    {
        private readonly ApplicationDbContext _db;
        public UserUiSettingsService(ApplicationDbContext db) => _db = db;

        public async Task<UserUiSettingsDTO> GetForUserAsync(int userId)
        {
            var s = await _db.UserUiSettings.FirstOrDefaultAsync(x => x.UserId == userId);
            if (s == null)
            {
                s = new UserUiSettings { UserId = userId }; // defaults
                _db.UserUiSettings.Add(s);
                await _db.SaveChangesAsync();
            }
            return Map(s);
        }

        public async Task<UserUiSettingsDTO> UpsertAsync(int userId, UserUiSettingsDTO dto)
        {
            var s = await _db.UserUiSettings.FirstOrDefaultAsync(x => x.UserId == userId);
            if (s == null)
            {
                s = new UserUiSettings { UserId = userId };
                _db.UserUiSettings.Add(s);
            }
            s.Theme = dto.Theme;
            s.ActiveTheme = dto.ActiveTheme;
            s.Horizontal = dto.Horizontal;
            s.CardBorder = dto.CardBorder;
            s.Boxed = dto.Boxed;
            s.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Map(s);
        }

        private static UserUiSettingsDTO Map(UserUiSettings s) => new()
        {
            Theme = s.Theme,
            ActiveTheme = s.ActiveTheme,
            Horizontal = s.Horizontal,
            CardBorder = s.CardBorder,
            Boxed = s.Boxed
        };
    }
}
