using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using TToApp.DTOs;
using TToApp.Model;
using Twilio.TwiML.Voice;

namespace TToApp.Model
{
    public class Warehouse
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string City { get; set; }

        [Required]
        [StringLength(100)]
        public string State { get; set; }

        [Required]
        [StringLength(200)]
        public string Address { get; set; }
        public string? ZipCode { get; set; }

        [Required]
        [StringLength(100)]
        public string Company { get; set; }

        public bool SendPayroll { get; set; } = false;
        public int? CompanyId { get; set; }

        // 🔹 Navigation Property
        public Company? Companie { get; set; }
        public bool IsHiring { get; set; } = false ;
        public bool AllowedExternalDrive { get; set; } = false;

        public List<Permits>? Permits { get; set; }

        public int? MetroId { get; set; }
        public Metro? Metro { get; set; }

        // Un almacén puede tener muchos usuarios (Managers y Drivers)

        [JsonIgnore]
        public ICollection<User> Users { get; set; } = new List<User>();
        public ICollection<Zone> Zones { get; set; } = new List<Zone>();
        public TimeOnly? OpenTime { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public PayrollConfig? PayrollConfig { get; set; }

        public decimal? DriveRate {  get; set; }


    }


}
