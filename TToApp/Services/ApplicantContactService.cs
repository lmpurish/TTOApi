using Microsoft.EntityFrameworkCore;
using TToApp.Model;
using TToApp.Services;
using static ApplicantContactService;

public interface IApplicantContactService
{
    Task<ApplicantContactResult>
        ContactApplicantAsync(int userId);
}

public class ApplicantContactService :  IApplicantContactService
{
    private readonly ApplicationDbContext _authContext;

    private readonly WhatsAppService _whatsAppService;

    private readonly EmailService _emailService;

    private readonly IAccountActivationService
        _accountActivationService;

    public ApplicantContactService(
        ApplicationDbContext authContext,
        WhatsAppService whatsAppService,
        EmailService emailService,
        IAccountActivationService accountActivationService)
    {
        _authContext = authContext;

        _whatsAppService =
            whatsAppService;

        _emailService =
            emailService;

        _accountActivationService =
            accountActivationService;
    }

    public async Task<ApplicantContactResult>
    ContactApplicantAsync(int userId)
    {
        // =====================================================
        // USER
        // =====================================================

        var user = await _authContext.Users
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(
                u => u.Id == userId
            );

        if (user is null)
        {
            return new ApplicantContactResult
            {
                Success = false,
                ErrorMessage = "User not found."
            };
        }

        // =====================================================
        // WAREHOUSE
        // =====================================================

        if (!user.WarehouseId.HasValue)
        {
            return new ApplicantContactResult
            {
                Success = false,
                ErrorMessage =
                    "User does not have a warehouse assigned."
            };
        }

        var warehouse =
            await _authContext.Warehouses
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    w =>
                        w.Id ==
                        user.WarehouseId.Value
                );

        if (warehouse is null)
        {
            return new ApplicantContactResult
            {
                Success = false,
                ErrorMessage =
                    "Warehouse not found."
            };
        }

        // =====================================================
        // MESSAGE TEMPLATE
        // =====================================================

        var wmt =
            await _authContext
                .WarehouseMessageTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    w =>
                        w.WarehouseId ==
                        user.WarehouseId.Value
                        &&
                        w.IsDefault
                );

        if (wmt is null)
        {
            return new ApplicantContactResult
            {
                Success = false,
                ErrorMessage =
                    "Message template not found."
            };
        }

        // =====================================================
        // PHONE
        // =====================================================

        var phone =
            user.Profile?.PhoneNumber;

        if (string.IsNullOrWhiteSpace(phone))
        {
            return new ApplicantContactResult
            {
                Success = false,
                ErrorMessage =
                    "User phone number is missing."
            };
        }

        // =====================================================
        // EMAIL
        // =====================================================

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return new ApplicantContactResult
            {
                Success = false,
                ErrorMessage =
                    "User email is missing."
            };
        }

        // =====================================================
        // WHATSAPP
        // NO ROMPE EL FLUJO
        // =====================================================

        try
        {
            _whatsAppService.EnviarMensaje(
                phone,
                wmt.MessageBody
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"WhatsApp failed for applicant {user.Id}: {ex.Message}"
            );
        }

        // =====================================================
        // FIRST CONTACT EMAIL
        // NO ROMPE EL FLUJO
        // =====================================================

        try
        {
            var firstContactSent =
                await _emailService.SendEmailAsync(
                    toEmail: user.Email,
                    subject: "Thank you!!",
                    "FirstContact.cshtml",
                    placeholders:
                        new Dictionary<string, string>
                        {
                            ["body"] =
                                wmt.MessageBody ?? "",

                            ["city"] =
                                warehouse.City ?? ""
                        },
                    copy: true
                );

            if (!firstContactSent)
            {
                Console.WriteLine(
                    $"FirstContact email returned false " +
                    $"for applicant {user.Id} - {user.Email}"
                );
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"FirstContact email failed for applicant {user.Id}: {ex.Message}"
            );
        }

        // =====================================================
        // ACCOUNT ACTIVATION
        // ESTA PARTE GENERA EL LINK
        // =====================================================

        AccountActivationResult activation;

        try
        {
            activation =
                await _accountActivationService
                    .SendAccountActivatedEmailAsync(
                        user,
                        "Account Activated!!"
                    );
        }
        catch (Exception ex)
        {
            return new ApplicantContactResult
            {
                Success = false,
                ErrorMessage =
                    $"Error activating account: {ex.Message}"
            };
        }

        // =====================================================
        // VALIDAR ACTIVACION
        // =====================================================

        if (!activation.Success)
        {
            return new ApplicantContactResult
            {
                Success = false,
                ErrorMessage =
                    activation.ErrorMessage
                    ?? "Account activation failed."
            };
        }

        // =====================================================
        // VALIDAR RESET LINK
        // =====================================================

        if (string.IsNullOrWhiteSpace(
            activation.ResetLink))
        {
            return new ApplicantContactResult
            {
                Success = false,
                ErrorMessage =
                    "Account activation succeeded but ResetLink was not generated."
            };
        }

        // =====================================================
        // GUARDAMOS LINK
        // =====================================================

        var setPasswordUrl =
            activation.ResetLink;

        // =====================================================
        // UPDATE USER
        // =====================================================

        user.WasContacted = true;
        user.IsActive = true;
        user.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _authContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            return new ApplicantContactResult
            {
                Success = false,

                // Aunque guardar el User falle,
                // devolvemos el link para debugging/información.
                SetPasswordUrl =
                    setPasswordUrl,

                ErrorMessage =
                    $"Error saving user: {ex.Message}"
            };
        }

        // =====================================================
        // SUCCESS
        // =====================================================

        return new ApplicantContactResult
        {
            Success = true,

            SetPasswordUrl =
                setPasswordUrl
        };
    }

    public sealed class ApplicantContactResult
    {
        public bool Success { get; set; }

        public string? ErrorMessage { get; set; }

        public string? SetPasswordUrl { get; set; }
    }
}