public sealed class PayRunLineDto
{
    public long Id { get; set; }
    public long PayRunId { get; set; }

    public string SourceType { get; set; } = null!;
    public string? SourceId { get; set; }
    public string? Description { get; set; }

    public decimal Qty { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }

    public string? Tags { get; set; }
    public DateTime? RouteDate { get; set; }

    public long? ZoneId { get; set; }
    public string? ZoneArea { get; set; }
}
