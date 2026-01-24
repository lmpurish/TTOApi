using System.ComponentModel.DataAnnotations;
using TToApp.Model;

namespace TToApp.DTOs;

public class PayrollBonusRuleCreateDto
{
    [Required]
    public int PayrollConfigId { get; set; }

    [Required]
    public BonusType Type { get; set; }

    public decimal? Threshold { get; set; }

    [Required]
    public decimal Amount { get; set; }

    public bool IsActive { get; set; } = true;
}

public class PayrollBonusRuleUpdateDto
{
    public BonusType? Type { get; set; }

    public decimal? Threshold { get; set; }

    public decimal? Amount { get; set; }

    public bool? IsActive { get; set; }
}

public class PayrollBonusRuleDto
{
    public int Id { get; set; }
    public int PayrollConfigId { get; set; }
    public BonusType Type { get; set; }
    public decimal? Threshold { get; set; }
    public decimal Amount { get; set; }
    public bool IsActive { get; set; }
}
