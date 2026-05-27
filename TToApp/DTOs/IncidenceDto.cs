namespace TToApp.DTOs
{
    public class AddIncidenceDto
    {
        public int RouteId { get; set; }
        public int UserId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ImageName { get; set; }
        public string? ImageUrl { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    public class IncidenceResponseDto
    {
        public int Id { get; set; }
        public int RouteId { get; set; }
        public int UserId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ImageName { get; set; }
        public string? ImageUrl { get; set; }
        public DateTime OccurredAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
