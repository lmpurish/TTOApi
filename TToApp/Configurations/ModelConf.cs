namespace TToApp.Configurations
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using TToApp.Model;

    public class ModelConf
    {
        public sealed class PayPeriodConfig : IEntityTypeConfiguration<PayPeriod>
        {
            public void Configure(EntityTypeBuilder<PayPeriod> b)
            {
                b.ToTable("PayPeriod");
                b.HasKey(x => x.Id);

                b.Property(x => x.CompanyId).IsRequired();
                b.Property(x => x.WarehouseId);

                // DateOnly → date
                b.Property(x => x.StartDate).HasColumnType("date").IsRequired();
                b.Property(x => x.EndDate).HasColumnType("date").IsRequired();

                b.Property(x => x.Status).HasMaxLength(16).HasDefaultValue("Open").IsRequired();
                b.Property(x => x.Notes).HasMaxLength(500);

                b.Property(x => x.CreatedBy).IsRequired();
                b.Property(x => x.CreatedAt)
                    .HasColumnType("datetime2")
                    .HasDefaultValueSql("SYSUTCDATETIME()")
                    .IsRequired();

                b.HasMany(x => x.PayRuns)
                    .WithOne(x => x.PayPeriod)
                    .HasForeignKey(x => x.PayPeriodId)
                    .OnDelete(DeleteBehavior.Restrict);
            }
        }

        public sealed class DriverRateConfig : IEntityTypeConfiguration<DriverRate>
        {
            public void Configure(EntityTypeBuilder<DriverRate> b)
            {
                b.ToTable("DriverRate");
                b.HasKey(x => x.Id);

                b.Property(x => x.DriverId).IsRequired();
                b.Property(x => x.RateType).HasMaxLength(16).IsRequired();

                b.Property(x => x.BaseAmount).HasColumnType("decimal(10,2)").IsRequired();
                b.Property(x => x.MinPayPerRoute).HasColumnType("decimal(10,2)");
                b.Property(x => x.OverStopBonusPerStop).HasColumnType("decimal(10,2)");
                b.Property(x => x.FailedStopPenalty).HasColumnType("decimal(10,2)");
                b.Property(x => x.RescueStopRate).HasColumnType("decimal(10,2)");
                b.Property(x => x.NightDeliveryBonus).HasColumnType("decimal(10,2)");

                b.Property(x => x.OverStopBonusThreshold);
                b.Property(x => x.EffectiveFrom).HasColumnType("date").IsRequired();
                b.Property(x => x.EffectiveTo).HasColumnType("date");
            }
        }

        public sealed class PayRunConfig : IEntityTypeConfiguration<PayRun>
        {
            public void Configure(EntityTypeBuilder<PayRun> b)
            {
                b.ToTable("PayRun");
                b.HasKey(x => x.Id);

                b.Property(x => x.PayPeriodId).IsRequired();
                b.Property(x => x.DriverId).IsRequired();

                b.Property(x => x.GrossAmount).HasColumnType("decimal(10,2)").HasDefaultValue(0m);
                b.Property(x => x.Adjustments).HasColumnType("decimal(10,2)").HasDefaultValue(0m);

                // Computada: NetAmount = GrossAmount + Adjustments
                b.Property(x => x.NetAmount)
                    .HasColumnType("decimal(10,2)")
                    .HasComputedColumnSql("[GrossAmount] + [Adjustments]", stored: true);

                b.Property(x => x.Status).HasMaxLength(16).HasDefaultValue("Draft").IsRequired();
                b.Property(x => x.CalculatedAt).HasColumnType("datetime2");
                b.Property(x => x.CalculatedBy);
            }
        }

        public sealed class PayRunLineConfig : IEntityTypeConfiguration<PayRunLine>
        {
            public void Configure(EntityTypeBuilder<PayRunLine> b)
            {
                b.ToTable("PayRunLine");
                b.HasKey(x => x.Id);

                b.Property(x => x.PayRunId).IsRequired();
                b.Property(x => x.SourceType).HasMaxLength(16).IsRequired();
                b.Property(x => x.SourceId).HasMaxLength(64);
                b.Property(x => x.Description).HasMaxLength(200);

                b.Property(x => x.Qty).HasColumnType("decimal(10,2)").HasDefaultValue(1m);
                b.Property(x => x.Rate).HasColumnType("decimal(10,2)").HasDefaultValue(0m);

                // Computada: Amount = Qty * Rate
                b.Property(x => x.Amount)
                    .HasColumnType("decimal(10,2)")
                    .HasComputedColumnSql("[Qty] * [Rate]", stored: true);

                b.Property(x => x.Tags).HasColumnType("nvarchar(max)");

                b.HasOne(x => x.PayRun)
                    .WithMany(x => x.Lines)
                    .HasForeignKey(x => x.PayRunId)
                    .OnDelete(DeleteBehavior.Cascade);
            }
        }

        public sealed class PayrollAdjustmentConfig : IEntityTypeConfiguration<PayrollAdjustment>
        {
            public void Configure(EntityTypeBuilder<PayrollAdjustment> b)
            {
                b.ToTable("PayrollAdjustment");
                b.HasKey(x => x.Id);

                b.Property(x => x.PayRunId).IsRequired();
                b.Property(x => x.Type).HasMaxLength(16).IsRequired();
                b.Property(x => x.Reason).HasMaxLength(300);
                b.Property(x => x.Amount).HasColumnType("decimal(10,2)").IsRequired();
                b.Property(x => x.CreatedBy).IsRequired();
                b.Property(x => x.CreatedAt)
                    .HasColumnType("datetime2")
                    .HasDefaultValueSql("SYSUTCDATETIME()")
                    .IsRequired();

                b.HasOne(x => x.PayRun)
                    .WithMany(x => x.AdjustmentsList)
                    .HasForeignKey(x => x.PayRunId)
                    .OnDelete(DeleteBehavior.Cascade);
            }
        }
    }
}
