using System.ComponentModel.DataAnnotations;

namespace TToApp.DTOs;

public class PayrollFineCreateDto
{

   // public int PackageId { get; set; }

    //public int UserId { get; set; }

    [Required, StringLength(100)]
    public string Tracking { get; set; }

    [Required, Range(0, double.MaxValue)]
    public decimal Amount { get; set; }

    [StringLength(50)]
    public string? Type { get; set; }

    [StringLength(255)]
    public string? Description { get; set; }
}
public class PayrollFineImportRequest
{
    [Required]
    public IFormFile File { get; set; } = null!;
}

public class PayrollFineUpdateDto
{
    // todo opcional para PATCH-like (pero usando PUT)
    public int? PackageId { get; set; }
    public int? UserId { get; set; }

    [StringLength(100)]
    public string? Tracking { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? Amount { get; set; }

    [StringLength(50)]
    public string? Type { get; set; }

    [StringLength(255)]
    public string? Description { get; set; }
}

public class PayrollFineDto
{
    public int Id { get; set; }
    public int PackageId { get; set; }
    public int UserId { get; set; }
    public string? Tracking { get; set; }
    public decimal Amount { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // opcional: datos relacionados “light”
    public string? UserName { get; set; }
    public string? PackageCode { get; set; }
}
