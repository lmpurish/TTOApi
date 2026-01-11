using Microsoft.EntityFrameworkCore;
using TToApp.Model;

public interface IApplicantContactService
{
    Task<(bool Success, string ErrorMessage)> ContactApplicantAsync(int userId);
}

public class ApplicantContactService : IApplicantContactService
{
    private readonly ApplicationDbContext _authContext;
    private readonly WhatsAppService _whatsAppService;
    private readonly EmailService _emailService;

    public ApplicantContactService(ApplicationDbContext authContext, WhatsAppService whatsAppService, EmailService emailService)
    {
        _authContext = authContext;
        _whatsAppService = whatsAppService;
        _emailService = emailService;
    }

    public async Task<(bool Success, string ErrorMessage)> ContactApplicantAsync(int userId)
    {
        var user = await _authContext.Users
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null) return (false, "User not found.");

        user.WasContacted = true;
        user.IsActive = true;
        user.UpdatedAt = DateTime.UtcNow;

        var wmt = await _authContext.WarehouseMessageTemplates
            .FirstOrDefaultAsync(w => w.WarehouseId == user.WarehouseId && w.IsDefault);
        if (wmt is null) return (false, "Message template not found.");

        var warehouse = await _authContext.Warehouses.FirstOrDefaultAsync(w => w.Id == user.WarehouseId);
        if (warehouse is null) return (false, "Warehouse not found.");

        // WhatsApp
        try
        {
            var phone = user.Profile?.PhoneNumber;
            if (string.IsNullOrWhiteSpace(phone))
                return (false, "User phone number is missing.");

            _whatsAppService.EnviarMensaje(phone, wmt.MessageBody);
        }
        catch (Exception ex)
        {
            return (false, $"Error sending WhatsApp message: {ex.Message}");
        }

        // Email
        try
        {
            if (string.IsNullOrWhiteSpace(user.Email))
                return (false, "User email is missing.");

            await _emailService.SendEmailAsync(
                toEmail: user.Email,
                subject: "Thank you!!",
                 "FirstContact.cshtml",
                placeholders: new Dictionary<string, string>
                {
                { "body", wmt.MessageBody },
                { "city", warehouse.City }
                },
                copy: true
            );
            await _emailService.SendEmailAsync(
                toEmail: user.Email,
                subject: "Account Activated!!",
                 "AccountActivated.cshtml",
                placeholders: new Dictionary<string, string>
                {
                { "Name", user.Name },
                { "LastName", user.LastName },

                },
                copy: true
            );
        }
        catch (Exception ex)
        {
            return (false, $"Error sending email: {ex.Message}");
        }

        // Persistir cambios
        try
        {
            await _authContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            return (false, $"Error saving to database: {ex.Message}");
        }

        return (true, string.Empty);
    }
}