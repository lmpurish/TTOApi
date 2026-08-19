using Microsoft.EntityFrameworkCore;
using TToApp.Migrations;
using TToApp.Model;
using static TToApp.Configurations.ModelConf;

namespace TToApp.Model
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
    {
        public DbSet<User> Users { get; set; }
        public DbSet<UserProfile> UserProfiles { get; set; }
        public DbSet<Warehouse> Warehouses { get; set; }
        public DbSet<Zone> Zones { get; set; }
        public DbSet<Routes> Routes { get; set; }
        public DbSet<Vehicle> Vehicles { get; set; }
        public DbSet<Packages> Packages { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<PackageReviewEvidence> PackageReviewEvidences { get; set; }
        public DbSet<WarehouseMessageTemplate> WarehouseMessageTemplates { get; set; }
        public DbSet<Company> Companies { get; set; }
        public DbSet<UserUiSettings> UserUiSettings { get; set; }
        public DbSet<CompanyDocumentTemplate> CompanyDocumentTemplates => Set<CompanyDocumentTemplate>();
        public DbSet<CompanyDocumentAssignment> CompanyDocumentAssignments => Set<CompanyDocumentAssignment>();
        public DbSet<UserDocumentSignature> UserDocumentSignatures => Set<UserDocumentSignature>();
        public DbSet<Accounts> Accounts { get; set; }
        public DbSet<PayPeriod> PayPeriods => Set<PayPeriod>();
        public DbSet<DriverRate> DriverRates => Set<DriverRate>();
        public DbSet<PayRun> PayRuns => Set<PayRun>();
        public DbSet<PayRunLine> PayRunLines => Set<PayRunLine>();
        public DbSet<PayrollAdjustment> PayrollAdjustments => Set<PayrollAdjustment>();
        public DbSet<ScheduleEvent> ScheduleEvents => Set<ScheduleEvent>();
        public DbSet<PayrollConfig> PayrollConfigs => Set<PayrollConfig>();
        public DbSet<PayrollWeightRule> PayrollWeightRules => Set<PayrollWeightRule>();
        public DbSet<Permits>  Permits => Set<Permits>();
        public DbSet<Metro> Metro => Set<Metro>();
        public DbSet<DriverPunch> DriverPunches => Set<DriverPunch>();
        public DbSet<PayrollFine> PayrollFines { get; set; }
        public DbSet<PayrollBonusRule> PayrollBonusRules { get; set; } = null!;
        public DbSet<RouteBonus> RouteBonuses { get; set; } = null!;
        public DbSet<PayrollPenaltyRule> PayrollPenaltyRules { get; set; } = null!;
        //public DbSet<PayrollConfig> PayrollConfigs { get; set; } = null!;
        public DbSet<EmployeeLoan> EmployeeLoans => Set<EmployeeLoan>();
        public DbSet<LoanRepayment> LoanRepayments => Set<LoanRepayment>();
        public DbSet<RentalVehicle> RentalVehicles => Set<RentalVehicle>();
        public DbSet<VehicleRental> VehicleRentals { get; set; }
        public DbSet<RentalRenter> RentalRenters { get; set; }
        public DbSet<VehicleImage> VehicleImages { get; set; }
        public DbSet<EarlyWarning> EarlyWarnings { get; set; }
        public DbSet<EarlyWarningConfig> EarlyWarningConfigs { get; set; }
        public DbSet<CommunicationRecipientRule> CommunicationRecipientRules { get; set; }
        public DbSet<Incidence> Incidences { get; set; }
        public DbSet<ZonePayRule> ZonePayRules { get; set; } = null!;
        public DbSet<ZoneWeightRule> ZoneWeightRules { get; set; } = null!;
        public DbSet<UserWarehouse> UserWarehouses { get; set; } = null!;
        public DbSet<AuditLogs> AuditLogs { get; set; }
        public DbSet<PackageReturnEvidence> PackageReturnEvidences { get; set; }
        public DbSet<CompanyRevenue> CompanyRevenues { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Company → Warehouses
            modelBuilder.Entity<Warehouse>()
                .HasOne(w => w.Companie)                // si tu prop real es Company, cámbiala aquí
                .WithMany(c => c.Warehouses)
                .HasForeignKey(w => w.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            // Warehouse → Users (SetNull)
            modelBuilder.Entity<Warehouse>()
                .HasMany(w => w.Users)
                .WithOne(u => u.Warehouse)
                .HasForeignKey(u => u.WarehouseId)
                .OnDelete(DeleteBehavior.SetNull);
            
            modelBuilder.Entity<Warehouse>().Property(w => w.DriveRate).HasPrecision(10, 4);

            // Warehouse → Zones
            modelBuilder.Entity<Zone>()
                .HasOne(z => z.Warehouse)
                .WithMany(w => w.Zones)
                .HasForeignKey(z => z.IdWarehouse)
                .OnDelete(DeleteBehavior.Cascade);

            // Company → Users
            modelBuilder.Entity<User>()
                .HasOne(u => u.Company)
                .WithMany(c => c.Users)
                .HasForeignKey(u => u.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            // ❗️UserRole: SIN conversiones (EF → int por defecto)
            // Si tu propiedad es nullable y DE VERDAD lo necesitas, usa SOLO esto:
            // modelBuilder.Entity<User>().Property(u => u.UserRole).HasConversion<int?>();

            // Company → Owner (User)
            modelBuilder.Entity<Company>()
                .HasOne(c => c.Owner)
                .WithOne()
                .HasForeignKey<Company>(c => c.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            // User ↔ Profile (1:1)
            modelBuilder.Entity<User>()
                .HasOne(u => u.Profile)
                .WithOne(p => p.User)
                .HasForeignKey<UserProfile>(p => p.Id)
                .OnDelete(DeleteBehavior.Cascade);

            // Enums a string (SOLO los que deben ir como texto)
            modelBuilder.Entity<Packages>().Property(p => p.Status).HasConversion<string>();
            modelBuilder.Entity<Packages>().Property(p => p.Weight).HasPrecision(10, 2);
            modelBuilder.Entity<Packages>().Property(p => p.ReviewStatus).HasConversion<string>();
            modelBuilder.Entity<Notification>().Property(n => n.Type).HasConversion<string>();

            // Notification → User
            modelBuilder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Company → Templates (NO ACTION)
            modelBuilder.Entity<CompanyDocumentTemplate>()
                .HasOne(t => t.Company)
                .WithMany(c => c.DocumentTemplates)
                .HasForeignKey(t => t.CompanyId)
                .OnDelete(DeleteBehavior.NoAction);

            // Signature → Template (NO ACTION)
            modelBuilder.Entity<UserDocumentSignature>()
                .HasOne(s => s.Template)
                .WithMany()
                .HasForeignKey(s => s.CompanyDocumentTemplateId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<PayrollFine>()
                .ToTable("PayrollFines");

            modelBuilder.Entity<PayrollFine>()
                .HasOne(f => f.User)
                .WithMany(u => u.PayrollFines)
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PayrollFine>()
                .HasOne(f => f.Package)
                .WithMany()
                .HasForeignKey(f => f.PackageId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<PayrollFine>()
                .HasOne(f => f.PayRun)
                .WithMany()
                .HasForeignKey(f => f.PayRunId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<PayrollFine>()
                .HasIndex(f => new { f.UserId, f.PackageId });

            modelBuilder.Entity<PayrollFine>()
                .HasIndex(f => f.Tracking);
                modelBuilder.Entity<PayrollFine>().Property(p => p.Amount).HasPrecision(10, 2);

            // Signature → User (NO ACTION)
            modelBuilder.Entity<UserDocumentSignature>()
                .HasOne(s => s.User)
                .WithMany(u => u.DocumentSignatures)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            // Assignment → Template/User (NO ACTION)
            modelBuilder.Entity<CompanyDocumentAssignment>()
                .HasOne(a => a.Template)
                .WithMany()
                .HasForeignKey(a => a.CompanyDocumentTemplateId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<CompanyDocumentAssignment>()
                .HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            // Índices
            modelBuilder.Entity<CompanyDocumentTemplate>()
                .HasIndex(t => new { t.CompanyId, t.IsActive, t.Version });

            modelBuilder.Entity<UserDocumentSignature>()
                .HasIndex(s => new { s.CompanyId, s.UserId, s.CompanyDocumentTemplateId });
            modelBuilder.Entity<Metro>()
                .HasOne(m => m.Company)
                .WithMany(c => c.Metros)      // asegúrate de tener ICollection<Metro> Metros en Company
                .HasForeignKey(m => m.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Warehouse>()
                .HasOne(w => w.PayrollConfig)
                .WithOne(pc => pc.Warehouse)
                .HasForeignKey<PayrollConfig>(pc => pc.WarehouseId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PayrollConfig>()
                .HasMany(x => x.WeightRules)
                .WithOne(x => x.PayrollConfig)
                .HasForeignKey(x => x.PayrollConfigId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PayrollConfig>()
                .HasMany(x => x.PenaltyRules)
                .WithOne(x => x.PayrollConfig)
                .HasForeignKey(x => x.PayrollConfigId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PayrollConfig>()
                .HasMany(x => x.BonusRules)
                .WithOne(x => x.PayrollConfig)
                .HasForeignKey(x => x.PayrollConfigId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PayrollConfig>()
                .HasIndex(x => x.WarehouseId)
                .IsUnique();

            modelBuilder.ApplyConfiguration(new PayPeriodConfig());
            modelBuilder.ApplyConfiguration(new DriverRateConfig());
            modelBuilder.ApplyConfiguration(new PayRunConfig());
            modelBuilder.ApplyConfiguration(new PayRunLineConfig());
            modelBuilder.ApplyConfiguration(new PayrollAdjustmentConfig());
            modelBuilder.Entity<PayrollConfig>().ToTable("PayrollConfigs");
            modelBuilder.Entity<PayrollWeightRule>().ToTable("PayrollWeightRules");
            
            modelBuilder.Entity<PayrollPenaltyRule>(entity =>
            {
                entity.ToTable("PayrollPenaltyRules"); // tabla plural
                entity.HasKey(x => x.Id)
                    .HasName("PK_PayrollPenaltyRule"); // PK real (singular)
                entity.HasIndex(x => new { x.PayrollConfigId, x.Type })
                    .HasDatabaseName("IX_PayrollPenaltyRule_PayrollConfigId_Type") // índice real
                    .IsUnique(); // ✅ no lleva parámetro
            });
       

            modelBuilder.Entity<PayrollBonusRule>().ToTable("PayrollBonusRules");

            modelBuilder.Entity<EmployeeLoan>(e =>
            {
                e.Property(x => x.Principal).HasPrecision(10,2);
                e.Property(x => x.Balance).HasPrecision(10,2);
                e.Property(x => x.InstallmentAmount).HasPrecision(10,2);
                e.Property(x => x.MaxDeductionPerPayRun).HasPrecision(10,2);

                e.HasIndex(x => new { x.DriverId, x.Status });
                e.HasIndex(x => x.Status);

            });
            modelBuilder.Entity<LoanRepayment>(e =>
            {
                e.Property(x => x.Amount).HasPrecision(10, 2);

                e.HasOne(x => x.Loan)
                    .WithMany(l => l.Repayments)
                    .HasForeignKey(x => x.LoanId)
                    .OnDelete(DeleteBehavior.Restrict); // requerido por default (LoanId no nullable)

                e.HasOne(x => x.PayRun)
                    .WithMany()
                    .HasForeignKey(x => x.PayRunId)
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired(false); // SOLO PayRunId opcional

                e.HasIndex(x => x.LoanId);
                e.HasIndex(x => new { x.PayRunId, x.DriverId }); // conserva el índice útil
            });
            modelBuilder.Entity<PayrollAdjustment>(e =>
            {
                e.Property(x => x.Amount).HasPrecision(10,2);
                // Si agregaste RefType/RefId, no hace falta nada extra.
                e.HasIndex(x => new { x.PayRunId, x.Type });
            });

            modelBuilder.Entity<EmployeeLoan>(e =>
            {
                e.HasOne(x => x.Driver)
                    .WithMany()
                    .HasForeignKey(x => x.DriverId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.CreatedBy)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.ApprovedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.ApprovedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });
            modelBuilder.Entity<RentalVehicle>()
               .HasOne(v => v.Metro)
               .WithMany()
               .HasForeignKey(v => v.MetroId)
               .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<RentalVehicle>()
                .HasOne(v => v.Company)
                .WithMany()
                .HasForeignKey(v => v.CompanyId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<RentalRenter>()
                .HasOne(x => x.User)
                .WithOne(x => x.RentalRenterProfile)
                .HasForeignKey<RentalRenter>(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VehicleRental>()
                .HasOne(x => x.RentalRenter)
                .WithMany(x => x.VehicleRentals)
                .HasForeignKey(x => x.RentalRenterId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VehicleRental>()
                .HasOne(x => x.RentalVehicle)
                .WithMany(x => x.Rentals)
                .HasForeignKey(x => x.RentalVehicleId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RentalVehicle>(entity =>
                {
                    entity.Property(x => x.Mileage)
                        .HasDefaultValue(0);
                });

            modelBuilder.Entity<VehicleRental>(entity =>
                {
                    entity.Property(x => x.StartMileage)
                        .HasDefaultValue(0);

                    entity.Property(x => x.EndMileage)
                        .IsRequired(false);

                    entity.HasCheckConstraint(
                        "CK_VehicleRentals_Mileage",
                        "[EndMileage] IS NULL OR [EndMileage] >= [StartMileage]"
                    );
                });
            modelBuilder.Entity<EarlyWarning>()
                .HasIndex(x => new
                {
                    x.CompanyId,
                    x.WarehouseId,
                    x.Type,
                    x.ReferenceDate,
                    x.DaysEvaluated
                })
                .IsUnique();

            modelBuilder.Entity<EarlyWarningConfig>()
                .HasIndex(x => new { x.CompanyId, x.WarehouseId, x.Type })
                .IsUnique();

            modelBuilder.Entity<RentalVehicle>()
            .HasMany(v => v.Images)
            .WithOne(i => i.Vehicle)
            .HasForeignKey(i => i.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);
            
            modelBuilder.Entity<EarlyWarning>()
            .HasOne(e => e.Warehouse)
            .WithMany(w => w.EarlyWarnings)
            .HasForeignKey(e => e.WarehouseId)
            .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<CommunicationRecipientRule>()
            .HasIndex(x => new
            {
                x.CompanyId,
                x.WarehouseId,
                x.EventType,
                x.Channel,
                x.Role
            })
            .IsUnique();

            modelBuilder.Entity<DriverRate>()
            .HasOne(r => r.Warehouse)
            .WithMany()
            .HasForeignKey(r => r.WarehouseId)
            .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<DriverRate>()
            .HasIndex(r => new
            {
                r.DriverId,
                r.WarehouseId,
                r.EffectiveFrom
            });

            modelBuilder.Entity<Incidence>()
                .Property(i => i.Type)
                .HasConversion<string>();

            modelBuilder.Entity<Incidence>()
                .HasOne(i => i.Route)
                .WithMany()
                .HasForeignKey(i => i.RouteId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Incidence>()
                .HasOne(i => i.User)
                .WithMany()
                .HasForeignKey(i => i.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Incidence>()
                .HasIndex(i => i.RouteId);

            modelBuilder.Entity<ZonePayRule>()
                .HasOne(z => z.Zone)
                .WithMany()
                .HasForeignKey(z => z.ZoneId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ZonePayRule>()
                .HasIndex(z => new { z.ZoneId, z.PaymentType, z.IsActive, z.EffectiveFrom });

            modelBuilder.Entity<ZoneWeightRule>()
                .HasOne(r => r.Zone)
                .WithMany()
                .HasForeignKey(r => r.ZoneId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ZoneWeightRule>()
                .HasIndex(r => new { r.ZoneId, r.IsActive, r.Priority });

            modelBuilder.Entity<CompanyRevenue>(e =>
            {
                e.HasKey(x => x.Id);

                e.Property(x => x.Revenue).HasColumnType("decimal(18,2)");
                e.Property(x => x.Expenses).HasColumnType("decimal(18,2)");
                e.Property(x => x.Adjustments).HasColumnType("decimal(18,2)");

                e.Property(x => x.RevenueType)
                    .HasMaxLength(50);

                e.HasOne(x => x.PayPeriod)
                    .WithMany(x => x.CompanyRevenues)
                    .HasForeignKey(x => x.PayPeriodId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Company)
                    .WithMany()
                    .HasForeignKey(x => x.CompanyId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Warehouse)
                    .WithMany()
                    .HasForeignKey(x => x.WarehouseId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<UserWarehouse>()
                .HasOne(uw => uw.User)
                .WithMany(u => u.UserWarehouses)
                .HasForeignKey(uw => uw.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserWarehouse>()
                .HasOne(uw => uw.Warehouse)
                .WithMany(w => w.UserWarehouses)
                .HasForeignKey(uw => uw.WarehouseId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserWarehouse>()
                .HasIndex(uw => new { uw.UserId, uw.WarehouseId })
                .IsUnique();

            modelBuilder.Entity<UserWarehouse>()
                .HasIndex(uw => new { uw.UserId, uw.IsActive });

            modelBuilder.Entity<CompanyRevenue>(entity =>
{
    entity.Property(x => x.Revenue)
        .HasPrecision(18, 2);

    entity.Property(x => x.Expenses)
        .HasPrecision(18, 2);

    entity.Property(x => x.Adjustments)
        .HasPrecision(18, 2);

    entity.Property(x => x.RevenueType)
        .HasMaxLength(50);

    entity.HasIndex(x => new
    {
        x.CompanyId,
        x.PayPeriodId,
        x.WarehouseId,
        x.RevenueType
    })
    .IsUnique();
});

        }
        public DbSet<TToApp.Model.ApplicantActivity> ApplicantActivity { get; set; } = default!;

    }
}
