using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace TToApp.Model
{
    public class Routes
    {
        [Key]
        public int Id { get; set; } // Identificador único de la ruta

        [Required]
        public DateTime Date { get; set; }

      
        [Required]
        public int DeliveryStops { get; set; } // Número total de paradas en la ruta

        [Required]
        public int Volumen { get; set; }
        [Required]
        public double Los { get; set; }
        [Required]
        public double CustomerOnTime { get; set; }
        [Required]
        public double BranchOnTime { get; set; }
        [Required]
        public int CNL { get; set; }

        public int? UserId {  get; set; }
        [ForeignKey("UserId")]
        public User? User { get; set; }

        public int Attempts { get; set; }

        public RouteStatus? routeStatus { get; set; }

        // Relación con Zone
        public int? ZoneId { get; set; } // Id del almacén asociado
        [ForeignKey("ZoneId")]
        public Zone? Zone { get; set; } // Referencia al almacén

        public PaymentType PaymentType { get; set; } = PaymentType.PerStop;

        public double? PriceRoute {  get; set; }
        public string? RouteCode{  get; set; }
      
        public int? WarehouseId { get; set; }
        public Warehouse? Warehouse { get; set; }
    }

    public enum PaymentType
    {
        PerRoute,
        PerStop,
        Mixed
    }
    public enum RouteStatus
    {
        Pending,    // La ruta está pendiente de asignación.
        Assigned,   // La ruta ya tiene un conductor asignado.
        InProgress, // La ruta está en curso.
        Completed,  // La ruta ha sido completada.
        Cancelled,  // La ruta fue cancelada.
        Delayed,    // La ruta está retrasada.
        Future,     // La ruta está programada para un futuro.
        Created,    // La ruta esta creada pero no es visible
        Available,   // La ruta esta disponible 
        Loading,
        PendingCompletion
    }
}
