namespace TToApp.Model
{
    public class Notification
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Message { get; set; }

        public NotificationType? Type { get; set; } // info, warning, success, error

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? ReadAt { get; set; }

        public string? Url { get; set; }

        public string? Source { get; set; }

        // Relación con el usuario
        public User? User { get; set; }
    }

    public enum NotificationType
    {
        Info,    // Received of Delivery
        Warning,   // Cancelled/Lost
        Success,   // Delivered
        Error,    // Returned
        System,
        Admin
    }
}
