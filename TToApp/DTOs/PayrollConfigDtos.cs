using System.ComponentModel.DataAnnotations;

namespace TToApp.DTOs;

public class PayrollConfigUpsertDto
{
    [Required]
    public int WarehouseId { get; set; }

    public bool EnableWeightExtra { get; set; } = false;
    public bool EnablePenalties { get; set; } = true;
    public bool EnableBonuses { get; set; } = false;

    public decimal DefaultPenaltyAmount { get; set; } = 0m;

    public decimal? PenaltyCapPerWeek { get; set; }

    public bool IsActive { get; set; } = true;
}

// “vista” de salida (sin Warehouse para evitar loops)
public class PayrollConfigDto
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }

    public bool EnableWeightExtra { get; set; }
    public bool EnablePenalties { get; set; }
    public bool EnableBonuses { get; set; }

    public decimal DefaultPenaltyAmount { get; set; }
    public decimal? PenaltyCapPerWeek { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public int WeightRulesCount { get; set; }
    public int PenaltyRulesCount { get; set; }
    public int BonusRulesCount { get; set; }
}
