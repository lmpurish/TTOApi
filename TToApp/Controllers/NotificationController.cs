using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Data.Entity;
using Microsoft.EntityFrameworkCore;
using TToApp.Model;

namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NotificationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public NotificationController(ApplicationDbContext context)
        {
            _context = context;
        }
        [HttpGet("{userId}")]
        public IActionResult GetUserNotifications(int userId)
        {
            var notifications = _context.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .ToList(); // ✅ funciona perfecto con EF6

            return Ok(notifications);
        }


        // 🔄 Marcar una notificación como leída
        [HttpPut("read/{notificationId}")]
        public async Task<IActionResult> MarkAsRead(int notificationId)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            if (notification == null)
                return NotFound();

            notification.IsRead = true;
            notification.ReadAt = DateTime.Now;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        // 📦 Crear una nueva notificación
        [HttpPost]
        public async Task<IActionResult> CreateNotification([FromBody] Notification notification)
        {
            var exists = _context.Users.Any(u => u.Id == notification.UserId);
            if (!exists)
                return BadRequest(new { Message = "El usuario especificado no existe." });

            notification.CreatedAt = DateTime.Now;
            notification.IsRead = false;

            _context.Notifications.Add(notification);
            _context.SaveChanges();

            return CreatedAtAction(nameof(GetUserNotifications), new { userId = notification.UserId }, notification);
        }
    }
}
