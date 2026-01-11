using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using TToApp.Model;

public class Zone
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string ZoneCode { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PriceStop { get; set; }

    [Required]
    public int IdWarehouse { get; set; }

    [ForeignKey(nameof(IdWarehouse))]
    public Warehouse? Warehouse { get; set; }

    [MaxLength(100)]
    public string? Area { get; set; }

    // Guardado en DB como string "77001,77002,77003"
    public string? ZipCodesSerialized { get; set; }

    [NotMapped]
    public List<string> ZipCodes
    {
        get => string.IsNullOrEmpty(ZipCodesSerialized)
            ? new List<string>()
            : ZipCodesSerialized.Split(',').ToList();
        set => ZipCodesSerialized = string.Join(",", value);
    }

  //  [JsonIgnore]
 //   public ICollection<Route> Routes { get; set; } = new List<Route>();
}
