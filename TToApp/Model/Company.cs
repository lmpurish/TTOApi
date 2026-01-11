using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TToApp.Model
{
    public class Company
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = null!;

        [MaxLength(500)]
        public string? LogoUrl { get; set; }

        public int? OwnerId { get; set; }             // FK → User
        public User? Owner { get; set; }             // mapeada Restrict en OnModelCreating

        public bool AllowsExternalDrivers { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required, MaxLength(200)]
        public string Email { get; set; } = null!;

        [MaxLength(50)]
        public string PhoneNumber { get; set; } = string.Empty;

        [MaxLength(400)]
        public string Address { get; set; } = string.Empty;

        // Stripe
        public string? StripeSubscriptionId { get; set; }
        public string? StripePriceId { get; set; }
        public ICollection<Metro>? Metros { get; set; } = new List<Metro>();

        // Relaciones
        [JsonIgnore] public ICollection<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
        [JsonIgnore] public ICollection<User> Users { get; set; } = new List<User>();
        public ICollection<CompanyDocumentTemplate> DocumentTemplates { get; set; } = [];
        // Plantillas de documentos (correcto)

        public string? ReferralCode { get; set; }
        public string? WebsiteUrl { get; set; }


    }
}
