using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Data.Entity;
using System.Security.Claims;
using TToApp.Model;

namespace TToApp.Controllers
{
    
    }

    public record CreateScheduleDto(
    int ApplicantId,
    int? WarehouseId,
    string Title,
    ScheduleType Type,
    DateTime StartAtUtc,
    DateTime EndAtUtc,
    string? TimeZoneIana,
    string? LocationLabel,
    string? LocationAddress,
    string? Notes,
    NotifyChannel NotifyToApplicant,
    NotifyChannel NotifyToRecruiter,
    TimeSpan? ReminderBefore1,
    TimeSpan? ReminderBefore2
);

    public class UpdateScheduleDto
    {
        public ScheduleStatus? Status { get; set; }
        public DateTime? StartAtUtc { get; set; }
        public DateTime? EndAtUtc { get; set; }
        public string? Title { get; set; }
    }

