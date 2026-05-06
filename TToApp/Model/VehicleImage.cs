using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TToApp.Model
{
    public class VehicleImage
    {
       
            [Key]
            public int Id { get; set; }

            [Required]
            public int VehicleId { get; set; }

            [ForeignKey(nameof(VehicleId))]
            [JsonIgnore]
            public RentalVehicle? Vehicle { get; set; }

            [Required]
            public int CompanyId { get; set; }

            [ForeignKey(nameof(CompanyId))]
            public Company? Company { get; set; }

            [Required]
            [StringLength(300)]
            public string ImageUrl { get; set; } = "";

            [StringLength(200)]
            public string? FileName { get; set; }

            public bool IsCover { get; set; } = false;

            public int SortOrder { get; set; } = 0;

            [StringLength(50)]
            public string? ImageType { get; set; }
            // gallery
            // cover
            // inspection
            // damage

            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }
    
}
