using System.ComponentModel.DataAnnotations;

namespace TToApp.DTOs
{
    public class UpdateVehicleStatusDto
    {
        [Required]
        public string Status { get; set; } = "";
    }
}
