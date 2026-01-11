
using System.Data.Entity;

namespace TToApp.Model
{
    public class Vehicle
    {
        public int Id { get; set; }
        public string Make { get; set; }
        public string Model { get; set; }
        public int? Year { get; set; }
        public string? Type {  get; set; }
        public bool IsDefault { get; set; } = false;
        public int UserId { get; set; }
        public User? User { get; set; }

        /// <summary>
        /// Marca este vehículo como predeterminado y apaga los demás del mismo usuario.
        /// Debes pasar el DbContext para que pueda ejecutar el cambio.
        /// </summary>
        public async Task MarkAsDefaultAsync(ApplicationDbContext ctx, CancellationToken ct = default)
        {
            if (ctx is null) throw new ArgumentNullException(nameof(ctx));
            if (UserId <= 0) throw new InvalidOperationException("UserId no válido en el vehículo.");

            // Apaga cualquier otro default del mismo usuario
#if EFCORE7_OR_GREATER
        await ctx.Vehicles
            .Where(v => v.UserId == this.UserId && v.Id != this.Id && v.IsDefault)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.IsDefault, false), ct);
#else
            var others = await ctx.Vehicles
                .Where(v => v.UserId == this.UserId && v.Id != this.Id && v.IsDefault)
                .ToListAsync(ct);
            foreach (var v in others) v.IsDefault = false;
#endif

            // Marca este como default (en el estado actual del contexto)
            this.IsDefault = true;

            // Si este vehículo aún no está siendo trackeado, adjúntalo como modificado sólo en IsDefault
            var entry = ctx.Entry(this);
            if (entry.State == Microsoft.EntityFrameworkCore.EntityState.Detached)
            {
                ctx.Vehicles.Attach(this);
                entry.Property(x => x.IsDefault).IsModified = true;
            }

            await ctx.SaveChangesAsync(ct);
        }
    }
}