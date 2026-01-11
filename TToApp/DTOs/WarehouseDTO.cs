using System.ComponentModel.DataAnnotations;
using TToApp.Model;

namespace TToApp.DTOs
{
    public class WarehouseDTO
    {
      

        [Required]
        public int Id { get; set; }
        public string City { get; set; } // Ciudad del almacén

        [Required]
        [StringLength(200)]
        public string Address { get; set; } // Dirección del almacén
        [StringLength(100)]
        public string State { get; set; }

        [Required]
        [StringLength(100)]
        public string Company { get; set; } // Compañía propietaria o administradora del almacén

        public int CompanyId { get; set; }

        // Auditoría
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

       
    }
}
