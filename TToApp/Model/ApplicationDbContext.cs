using Microsoft.EntityFrameworkCore;
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

        public DbSet<PayrollPenaltyRule> PayrollPenaltyRules { get; set; } = null!;
        //public DbSet<PayrollConfig> PayrollConfigs { get; set; } = null!;

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
                .HasIndex(f => new { f.UserId, f.PackageId });

            modelBuilder.Entity<PayrollFine>()
                .HasIndex(f => f.Tracking);

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
        }
        public DbSet<TToApp.Model.ApplicantActivity> ApplicantActivity { get; set; } = default!;

    }
}
