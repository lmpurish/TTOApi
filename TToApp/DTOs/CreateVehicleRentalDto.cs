using System.ComponentModel.DataAnnotations;

namespace TToApp.DTOs
{
    public class CreateVehicleRentalDto
{
    [Required]
    public int RentalVehicleId { get; set; }

    [Required]
    public int RentalRenterId { get; set; }

    [Required]
    public DateOnly StartDate { get; set; }

    [Required]
    public DateOnly EndDate { get; set; }

    public string? Notes { get; set; }
}
}