using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using TToApp.DTOs;

namespace TToApp.Model;

public class PayrollFine
{
    public enum PayrollFineType
    {
        Internal_Audit = 1,
        Claim_confirmed_to_client  = 2,
        MissingPackage = 3,
        RouteViolation = 4,
        Other = 99
    }

    [Key]
    public int Id { get; set; }

    [Required]
    public int PackageId { get; set; }

    [Required]
    public int UserId { get; set; }          

    [StringLength(100)]
    public string? Tracking { get; set; }

    public decimal Amount { get; set; }

    [Required]
    [StringLength(50)]
    public string Type { get; set; } = string.Empty;

    [StringLength(255)]
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
   // public PayrollFineType Type { get; set; } = PayrollFineType.Other;

    public User User { get; set; } = null!;
    public Packages Package { get; set; } = null!;
}
