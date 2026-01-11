using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using TToApp.Model;

namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AccountsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/Accounts
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Accounts>>> GetAccounts()
        {
            return await _context.Accounts.ToListAsync();
        }

        // GET: api/Accounts/5
        

        // PUT: api/Accounts/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id:int}")]
        public async Task<IActionResult> PutAccounts(int id, [FromBody] AccountsUpdateDto dto)
        {
            // 1) Usuario autenticado
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim is null) return Unauthorized("Invalid user.");
            if (!int.TryParse(userIdClaim.Value, out var userId)) return Unauthorized("Invalid user.");

            // 2) Buscar cuenta del usuario
            var account = await _context.Accounts
                .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);

            if (account is null) return NotFound(new { Message = "Account not found." });

            // 3) Validaciones básicas (solo si vienen valores)
            if (!string.IsNullOrWhiteSpace(dto.AccountNumber))
            {
                // Ejemplo de validación simple; ajusta a tu formato real
                var acc = dto.AccountNumber.Trim();
                if (acc.Length < 6) return BadRequest(new { Message = "AccountNumber is too short." });
                account.AccountNumber = acc;
            }

            if (!string.IsNullOrWhiteSpace(dto.RoutingNumber))
            {
                var rt = dto.RoutingNumber.Trim();
                if (rt.Length < 6) return BadRequest(new { Message = "RoutingNumber is too short." });
                account.RoutingNumber = rt;
            }

            if (!string.IsNullOrWhiteSpace(dto.FullName))
            {
                account.FullName = dto.FullName.Trim();
            }

            // 4) Manejo de IsDefault
            if (dto.IsDefault.HasValue && dto.IsDefault.Value)
            {
                // Si esta cuenta será la predeterminada, desmarcar las demás del usuario
                var others = _context.Accounts.Where(a => a.UserId == userId && a.Id != account.Id && a.IsDefault);
                await others.ForEachAsync(a => a.IsDefault = false);
                account.IsDefault = true;
            }
            else if (dto.IsDefault.HasValue && !dto.IsDefault.Value)
            {
                // Permitir quitar default; aseguramos que al menos 1 quede como default
                account.IsDefault = false;

                bool anyDefault = await _context.Accounts.AnyAsync(a => a.UserId == userId && a.IsDefault && a.Id != account.Id);
                if (!anyDefault)
                {
                    // Promueve otra cuenta si existe
                    var firstOther = await _context.Accounts
                        .Where(a => a.UserId == userId && a.Id != account.Id)
                        .OrderBy(a => a.Id)
                        .FirstOrDefaultAsync();

                    if (firstOther != null)
                        firstOther.IsDefault = true;
                    else
                        account.IsDefault = true; // Si no hay otras cuentas, mantenla default
                }
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return StatusCode(409, new { Message = "Concurrency conflict while updating the account." });
            }

            // 5) Respuesta (enmascarar datos sensibles)
            var result = new
            {
                account.Id,
                account.FullName,
                AccountNumber = account.AccountNumber,
                RoutingNumber = account.RoutingNumber,
                account.IsDefault
            };

            return Ok(result);
        }

        private static string Mask(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var last4 = value.Length >= 4 ? value[^4..] : value;
            return new string('•', Math.Max(0, value.Length - last4.Length)) + last4;
        }

        // POST: api/Accounts
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<object>> PostAccounts([FromBody] AccountsCreateDto dto)
        {
            // 1) Usuario autenticado
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim is null || !int.TryParse(userIdClaim.Value, out var userId))
                return Unauthorized("Invalid user.");

            // 2) Validaciones mínimas
            if (string.IsNullOrWhiteSpace(dto.AccountNumber) ||
                string.IsNullOrWhiteSpace(dto.RoutingNumber) ||
                string.IsNullOrWhiteSpace(dto.FullName))
                return BadRequest(new { Message = "AccountNumber, RoutingNumber and FullName are required." });

            // (opcional) reglas simples de longitud; ajusta a tu caso real
            if (dto.AccountNumber.Trim().Length < 6) return BadRequest(new { Message = "AccountNumber is too short." });
            if (dto.RoutingNumber.Trim().Length < 6) return BadRequest(new { Message = "RoutingNumber is too short." });

            // 3) Si se marca como default, desmarcar otras del usuario
            if (dto.IsDefault)
            {
                var others = _context.Accounts.Where(a => a.UserId == userId && a.IsDefault);
                await others.ForEachAsync(a => a.IsDefault = false);
            }
            else
            {
                // si no viene default y el usuario no tiene ninguna, esta será default
                bool hasDefault = await _context.Accounts.AnyAsync(a => a.UserId == userId && a.IsDefault);
                if (!hasDefault) dto.IsDefault = true;
            }

            // 4) Crear entidad fijando el UserId desde el token (evita overposting)
            var account = new Accounts
            {
                UserId = userId,
                AccountNumber = dto.AccountNumber.Trim(),
                RoutingNumber = dto.RoutingNumber.Trim(),
                FullName = dto.FullName.Trim(),
                IsDefault = dto.IsDefault
            };

            _context.Accounts.Add(account);
            await _context.SaveChangesAsync();

            // 5) Devuelve 201 con Location (GetAccounts) y el objeto creado
            return CreatedAtAction(
                nameof(GetAccounts),        // asegúrate de tener este GET por id
                new { id = account.Id },
                new
                {
                    account.Id,
                    account.FullName,
                    account.AccountNumber,
                    account.RoutingNumber,
                    account.IsDefault
                }
            );
        }

        // Ejemplo de GET por id (para CreatedAtAction)
        [HttpGet("{id:int}")]
        public async Task<ActionResult<object>> GetAccounts(int id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim is null || !int.TryParse(userIdClaim.Value, out var userId))
                return Unauthorized("Invalid user.");

            var acc = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
            if (acc is null) return NotFound();

            return Ok(new
            {
                acc.Id,
                acc.FullName,
                acc.AccountNumber,
                acc.RoutingNumber,
                acc.IsDefault
            });
        }

        // DELETE: api/Accounts/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAccounts(int id)
        {
            var accounts = await _context.Accounts.FindAsync(id);
            if (accounts == null)
            {
                return NotFound();
            }

            _context.Accounts.Remove(accounts);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool AccountsExists(int id)
        {
            return _context.Accounts.Any(e => e.Id == id);
        }

        public class AccountsUpdateDto
        {
            public string? AccountNumber { get; set; }
            public string? RoutingNumber { get; set; }
            public string? FullName { get; set; }
            public bool? IsDefault { get; set; } // null = no cambiar
        }
        public class AccountsCreateDto
        {
            public string AccountNumber { get; set; } = "";
            public string RoutingNumber { get; set; } = "";
            public string FullName { get; set; } = "";
            public bool IsDefault { get; set; } = true; // por defecto, la primera será default
        }


    }
}
