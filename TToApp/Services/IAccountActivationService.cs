using Microsoft.EntityFrameworkCore;
using TToApp.Model;

namespace TToApp.Services
{
    public interface IAccountActivationService
    {
        Task<AccountActivationResult> SendAccountActivatedEmailAsync(
            User user,
            string subject);
    }

    public class AccountActivationService : IAccountActivationService
    {
        private readonly ApplicationDbContext _authContext;
        private readonly EmailService _emailService;

        public AccountActivationService(
            ApplicationDbContext authContext,
            EmailService emailService)
        {
            _authContext = authContext;
            _emailService = emailService;
        }

        public async Task<AccountActivationResult>
    SendAccountActivatedEmailAsync(
        User user,
        string subject)
        {
            if (user == null)
            {
                return new AccountActivationResult
                {
                    Success = false,
                    ErrorMessage = "User is required."
                };
            }

            if (string.IsNullOrWhiteSpace(user.Email))
            {
                return new AccountActivationResult
                {
                    Success = false,
                    ErrorMessage = "User email is required."
                };
            }

            // =====================================================
            // COMPANY
            // =====================================================

            string companyName = "TTO Logistics";

            if (user.CompanyId.HasValue)
            {
                var company = await _authContext.Companies
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        c => c.Id == user.CompanyId.Value
                    );

                if (company != null &&
                    !string.IsNullOrWhiteSpace(company.Name))
                {
                    companyName = company.Name;
                }
            }

            // =====================================================
            // PASSWORD RESET TOKEN
            // =====================================================

            var resetToken =
                Guid.NewGuid().ToString("N");

            user.PasswordResetToken =
                resetToken;

            user.PasswordResetTokenExpiresAt =
                DateTime.UtcNow.AddHours(24);

            user.UpdatedAt =
                DateTime.UtcNow;

            // =====================================================
            // RESET LINK
            // =====================================================

            var resetLink =
                "https://admin.ttologistics.com/" +
                "authentication/set-password" +
                $"?token={Uri.EscapeDataString(resetToken)}";

            // =====================================================
            // GUARDAR TOKEN
            // ESTA PARTE ES CRITICA
            // =====================================================

            try
            {
                await _authContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                return new AccountActivationResult
                {
                    Success = false,
                    ErrorMessage =
                        $"Could not save activation token: {ex.Message}"
                };
            }

            // =====================================================
            // A PARTIR DE AQUI EL LINK YA EXISTE
            // EL EMAIL NO PUEDE ROMPER EL FLUJO
            // =====================================================

            var placeholders =
                new Dictionary<string, string>
                {
                    ["Name"] =
                        user.Name ?? "",

                    ["LastName"] =
                        user.LastName ?? "",

                    ["Email"] =
                        user.Email ?? "",

                    ["CompanyName"] =
                        companyName,

                    ["ResetLink"] =
                        resetLink
                };

            // =====================================================
            // SEND EMAIL
            // OPCIONAL
            // =====================================================

            bool emailSent = false;

            try
            {
                emailSent =
                    await _emailService.SendEmailAsync(
                        toEmail: user.Email,
                        subject: subject,
                        "AccountActivated.cshtml",
                        placeholders: placeholders,
                        copy: true
                    );

                if (!emailSent)
                {
                    Console.WriteLine(
                        $"Account activation email could not be sent " +
                        $"to user {user.Id} - {user.Email}"
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Account activation email failed " +
                    $"for user {user.Id}: {ex.Message}"
                );
            }

            // =====================================================
            // SUCCESS
            // EL TOKEN FUE CREADO Y GUARDADO
            // =====================================================

            return new AccountActivationResult
            {
                Success = true,
                ResetLink = resetLink

                // Si agregas EmailSent al modelo:
                // EmailSent = emailSent
            };
        }
    }

        public sealed class AccountActivationResult
        {
            public bool Success { get; set; }

            public string? ResetLink { get; set; }

            public string? ErrorMessage { get; set; }
        }
    }
