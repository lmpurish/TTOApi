using System.Text.Json.Serialization;

namespace TToApp.Model
{
    public class PayRunLine
    {
        public long Id { get; set; }
        public long PayRunId { get; set; }
        public string SourceType { get; set; } = null!; // Route|Stop|Package|ManualAdj
        public string? SourceId { get; set; }
        public string? Description { get; set; }
        public decimal Qty { get; set; }
        public decimal Rate { get; set; }
        public decimal Amount { get; private set; } // Computada
        public string? Tags { get; set; }

        [JsonIgnore]                 // <-- rompe el ciclo
        public PayRun PayRun { get; set; } = null!;
    }
}
