namespace TToApp.Model
{
    public enum ScheduleType { PhoneScreen, Interview, DocsReview, RideAlong, StartDate, FollowUp }
    public enum ScheduleStatus { Scheduled, Rescheduled, Completed, NoShow, Canceled }
    [Flags] public enum NotifyChannel { None = 0, Email = 1, WhatsApp = 2, SMS = 4 }

    public class ScheduleEvent
    {
        public int Id { get; set; }

        public int ApplicantId { get; set; }
        public int RecruiterUserId { get; set; }           // User.Id del recruiter
        public int? WarehouseId { get; set; }

        public string Title { get; set; } = default!;      // “Phone screen”, “Onboarding”, etc.
        public ScheduleType Type { get; set; }
        public ScheduleStatus Status { get; set; } = ScheduleStatus.Scheduled;

        public DateTime StartAtUtc { get; set; }
        public DateTime EndAtUtc { get; set; }
        public string TimeZoneIana { get; set; } = "America/Chicago";

        public string? LocationLabel { get; set; }         // “Office”, “Phone”, “Google Meet…”
        public string? LocationAddress { get; set; }       // dirección o link
        public string? Notes { get; set; }

        // Notificaciones
        public NotifyChannel NotifyToApplicant { get; set; } = NotifyChannel.WhatsApp | NotifyChannel.Email;
        public NotifyChannel NotifyToRecruiter { get; set; } = NotifyChannel.Email;

        // Recordatorios (opcional)
        public TimeSpan? ReminderBefore1 { get; set; } = TimeSpan.FromHours(24);
        public TimeSpan? ReminderBefore2 { get; set; } = TimeSpan.FromHours(1);

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public int CreatedByUserId { get; set; }
    }

}
