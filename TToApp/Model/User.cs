using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using TToApp.Model;

public class User
{
    public enum Role
    {
        Admin,
        Manager,
        Assistant,
        Driver,
        Rsp,
        Applicant,
        CompanyOwner,
        Recruiter
    }

    public enum HiringStage
    {
        New,
        Contact_Attempted ,
        Phone_Screen,
        Docs_Pending,
        Approved_For_Hire,
        Hired,
        Rejected
    }

    [Key]
    public int Id { get; set; }

    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    public Role? UserRole { get; set; } = Role.Applicant;

    [StringLength(50)]
    public string? IdentificationNumber { get; set; }


    [Required]
    [EmailAddress]
    [StringLength(100)]
    public string? Email { get; set; }

    [Required]
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    public string? Token { get; set; }
    public bool IsActive { get; set; } = false;
    public bool IsFirstLogin { get; set; } = true;
    public bool WasContacted { get; set; } = false;
    public bool AcceptsSMSNotifications { get; set; } = false;

    public string? AvatarUrl { get; set; }
   
    public int? WarehouseId { get; set; }

    public Warehouse? Warehouse { get; set; }

    public int? CompanyId { get; set; }
    public Company? Company { get; set; }

    public UserProfile? Profile { get; set; }
   
    [JsonIgnore]
    public ICollection<Routes>? Routes { get; set; }
    public string? StripeCustomerId { get; set; }
    public string? StripePaymentMethodId { get; set; }
    public string? StripeAccountId { get; set; }
    public bool StripeAccountVerified { get; set; }

    public int? RecruiterId { get; set; }
    public User? Recruiter { get; set; }
    public HiringStage? Stage { get; set; } = HiringStage.New;
    public int? MetroId { get; set; }
    public Metro? Metro { get; set; }


    [JsonIgnore]
    public ICollection<Vehicle>? Vehicles { get; set; }

    [JsonIgnore]
    public ICollection<Accounts>? Accounts { get; set; }

    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateOnly? InitialDate { get; set; }
    public DateOnly? ConfirmationDate { get; set; }
    public string? ConfirmationToken { get; set; }
    public ICollection<UserDocumentSignature> DocumentSignatures { get; set; } = [];

}
