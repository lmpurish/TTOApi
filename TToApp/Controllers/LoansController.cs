using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TToApp.DTOs;
using TToApp.Model;

namespace TToApp.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/loans")]
    public class LoansController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        public LoansController(ApplicationDbContext db) => _db = db;

        [HttpPost]
        public async Task<ActionResult<LoanDto>> Create([FromBody] LoanDto req)
        {
            if (req.Principal <= 0) return BadRequest("Principal debe ser > 0.");
            if (req.InstallmentAmount <= 0)
                return BadRequest("InstallmentAmount debe ser > 0.");
            if (req.MaxDeductionPerPayRun.HasValue && req.MaxDeductionPerPayRun <= 0) return BadRequest("MaxDeductionPerPayRun debe ser > 0.");

            // opcional: bloquear si ya tiene uno activo
            // var hasActive = await _db.EmployeeLoans.AnyAsync(l => l.DriverId == req.DriverId && l.Status == "Active" && l.Balance > 0);
            // if (hasActive) return BadRequest("El driver ya tiene un préstamo activo.");

            long userId;
            try
            {
                userId = GetUserId();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ex.Message);
            }

            // Opcional: Verificar que el usuario existe en DB para evitar el error FK si borraste al usuario
            var userExists = await _db.Users.AnyAsync(u => u.Id == userId);
            if (!userExists) return BadRequest("El usuario que intenta crear el préstamo no existe en el sistema.");

            var loan = new EmployeeLoan
            {
                DriverId = (int)req.DriverId,
                Principal = req.Principal,
                Balance = req.InstallmentAmount,
                InstallmentAmount = req.InstallmentAmount,
                MaxDeductionPerPayRun = req.MaxDeductionPerPayRun,
                Notes = req.Notes,
                Status = "Draft",
                CreatedBy = (int)userId,
                CreatedAt = DateTime.UtcNow
            };

            _db.EmployeeLoans.Add(loan);
            await _db.SaveChangesAsync();

            return Ok(ToDto(loan));
        }

        [HttpPost("{id:long}/approve")]
        public async Task<ActionResult<LoanDto>> Approve(long id)
        {
            var userId = GetUserId();

            var loan = await _db.EmployeeLoans.FirstOrDefaultAsync(l => l.Id == id);
            if (loan is null) return NotFound();

            if (loan.Status == "Cancelled" || loan.Status == "Completed")
                return BadRequest($"No se puede aprobar un préstamo en estado {loan.Status}.");

            loan.Status = "Active";
            loan.ApprovedBy = (int?)userId;
            loan.ApprovedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return Ok(ToDto(loan));
        }

        [HttpPost("{id:long}/pause")]
        public async Task<ActionResult<LoanDto>> Pause(long id)
        {
            var loan = await _db.EmployeeLoans.FirstOrDefaultAsync(l => l.Id == id);
            if (loan is null) return NotFound();

            if (loan.Status != "Active")
                return BadRequest("Solo se puede pausar un préstamo Active.");

            loan.Status = "Paused";
            await _db.SaveChangesAsync();
            return Ok(ToDto(loan));
        }

        [HttpPost("{id:long}/resume")]
        public async Task<ActionResult<LoanDto>> Resume(long id)
        {
            var loan = await _db.EmployeeLoans.FirstOrDefaultAsync(l => l.Id == id);
            if (loan is null) return NotFound();

            if (loan.Status != "Paused")
                return BadRequest("Solo se puede reanudar un préstamo Paused.");

            loan.Status = "Active";
            await _db.SaveChangesAsync();
            return Ok(ToDto(loan));
        }

        [HttpPost("{id:long}/cancel")]
        public async Task<ActionResult<LoanDto>> Cancel(long id)
        {
            var loan = await _db.EmployeeLoans.FirstOrDefaultAsync(l => l.Id == id);
            if (loan is null) return NotFound();

            if (loan.Status == "Completed")
                return BadRequest("No se puede cancelar un préstamo Completed.");

            // regla: solo si no se ha cobrado nada
            var hasRepayments = await _db.LoanRepayments.AnyAsync(r => r.LoanId == id && r.Status == "Applied");
            if (hasRepayments) return BadRequest("No se puede cancelar: ya tiene cobros aplicados.");

            loan.Status = "Cancelled";
            await _db.SaveChangesAsync();
            return Ok(ToDto(loan));
        }

        [HttpGet("{id:long}")]
        public async Task<ActionResult<object>> Get(long id)
        {
            var loan = await _db.EmployeeLoans
                .AsNoTracking()
                .Include(l => l.Driver)
                .Include(l => l.Repayments)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (loan is null) return NotFound();

            return Ok(new
            {
                Loan = ToDto(loan),
                Repayments = loan.Repayments
                    .OrderByDescending(r => r.AppliedAt)
                    .Select(r => new
                    {
                        r.Id,
                        r.PayRunId,
                        r.Amount,
                        r.Status,
                        r.AppliedAt,
                        r.ReversedAt,
                        r.Reason
                    })
            });
        }
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetAll()
        {
            var loans = await _db.EmployeeLoans
                .AsNoTracking()
                .Include(l => l.Driver) // 👈 relación con User/Employee
                .Include(l => l.Repayments)
                .ToListAsync();

            return Ok(loans.Select(loan => new
            {
                Loan = ToDto(loan),
                Repayments = loan.Repayments
                    .OrderByDescending(r => r.AppliedAt)
                    .Select(r => new
                    {
                        r.Id,
                        r.PayRunId,
                        r.Amount,
                        r.Status,
                        r.AppliedAt,
                        r.ReversedAt,
                        r.Reason
                    })
            }));
        }


        [HttpGet("driver/{driverId:long}")]
        public async Task<ActionResult<IEnumerable<LoanDto>>> GetByDriver(long driverId)
        {
            var loans = await _db.EmployeeLoans
                .AsNoTracking()
                .Where(l => l.DriverId == driverId)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            return Ok(loans.Select(ToDto));
        }

        [HttpPost("{id:long}/manual-payment")]
        public async Task<ActionResult> ManualPayment(long id, [FromBody] ManualLoanPaymentRequestDto req)
        {
            if (req.Amount <= 0) return BadRequest("Amount debe ser > 0.");

            var userId = GetUserId(); // tu método real
            var paidAt = req.PaidAtUtc ?? DateTime.UtcNow;

            await using var tx = await _db.Database.BeginTransactionAsync();

            var loan = await _db.EmployeeLoans
                .FirstOrDefaultAsync(l => l.Id == id);

            if (loan is null) return NotFound();
            if (loan.Status is "Cancelled") return BadRequest("Loan está Cancelled.");
            if (loan.Balance <= 0 || loan.Status is "Completed")
                return BadRequest("Loan ya está pagado (Completed).");

            // Solo permitir pagos manuales si está Active o Paused (tú decides)
            if (loan.Status is not ("Active" or "Paused"))
                return BadRequest($"No se puede pagar manual en estado {loan.Status}.");

            var amountToApply = Math.Min(req.Amount, loan.Balance);
            if (amountToApply <= 0) return BadRequest("No hay saldo para aplicar.");

            // Crear repayment manual (sin PayRunId)
            var repayment = new LoanRepayment
            {
                LoanId = loan.Id,
                PayRunId = null,                // <-- manual
                DriverId = loan.DriverId,
                Amount = amountToApply,
                Status = "Applied",
                AppliedBy = userId,
                AppliedAt = paidAt,
                Reason = string.IsNullOrWhiteSpace(req.Reason) ? "manual" : req.Reason.Trim()
            };
            _db.LoanRepayments.Add(repayment);

            // Actualizar saldo
            loan.Balance -= amountToApply;
            if (loan.Balance == 0)
                loan.Status = "Completed";

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new
            {
                LoanId = loan.Id,
                Applied = amountToApply,
                RemainingBalance = loan.Balance,
                LoanStatus = loan.Status,
                RepaymentId = repayment.Id
            });
        }

        [HttpPost("repayments/{repaymentId:long}/reverse")]
        public async Task<ActionResult> ReverseManualRepayment(long repaymentId, [FromBody] string? reason)
        {
            var userId = GetUserId();

            await using var tx = await _db.Database.BeginTransactionAsync();

            var rep = await _db.LoanRepayments.FirstOrDefaultAsync(r => r.Id == repaymentId);
            if (rep is null) return NotFound();

            if (rep.Status != "Applied") return BadRequest("Repayment no está Applied.");
            if (rep.PayRunId != null) return BadRequest("Solo se puede revertir pagos manuales.");

            var loan = await _db.EmployeeLoans.FirstOrDefaultAsync(l => l.Id == rep.LoanId);
            if (loan is null) return BadRequest("Loan no existe.");

            // Revertir
            rep.Status = "Reversed";
            rep.ReversedAt = DateTime.UtcNow;
            rep.ReversedBy = userId;
            rep.Reason = string.IsNullOrWhiteSpace(reason) ? "reversed" : reason;

            loan.Balance += rep.Amount;
            if (loan.Status == "Completed" && loan.Balance > 0)
                loan.Status = "Active";

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new
            {
                RepaymentId = rep.Id,
                RepaymentStatus = rep.Status,
                LoanId = loan.Id,
                LoanBalance = loan.Balance,
                LoanStatus = loan.Status
            });
        }



           private static LoanDto ToDto(EmployeeLoan l) => new()
           {
               Id = l.Id,
               // 🔹 Nombre del driver
               DriverId = l.DriverId,
               Driver = l.Driver != null
        ? $"{l.Driver.Name} {l.Driver.LastName}".Trim()
        : null,

               Principal = l.Principal,
               Balance = l.Balance,
               InstallmentAmount = l.InstallmentAmount,
               MaxDeductionPerPayRun = l.MaxDeductionPerPayRun,
               Status = l.Status,
               Notes = l.Notes,
               CreatedAt = l.CreatedAt,
               ApprovedAt = l.ApprovedAt
           };

        private long GetUserId()
        {
            // Intentamos obtener el ID del claim estándar de NameIdentifier
            var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                        ?? User.FindFirst("nameid");

            if (claim != null && long.TryParse(claim.Value, out long id))
            {
                return id;
            }

            // Si llegas aquí, el token no es válido o no tiene ID
            // Es mejor lanzar una excepción para que el middleware de error la capture
            throw new UnauthorizedAccessException("El token no contiene un ID de usuario válido.");
        }
    }
}
