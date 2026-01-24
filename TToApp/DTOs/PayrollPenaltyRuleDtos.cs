using System.ComponentModel.DataAnnotations;
using TToApp.Model;

namespace TToApp.DTOs;

public class PayrollPenaltyRuleCreateDto
{
    [Required]
    public int PayrollConfigId { get; set; }

    [Required]
    public PenaltyType Type { get; set; }

    [Required]
    public decimal Amount { get; set; }

    public bool ApplyPerOccurrence { get; set; } = true;

    public int? MaxOccurrencesPerWeek { get; set; }

    public bool IsActive { get; set; } = true;
}

public class PayrollPenaltyRuleUpdateDto
{
    public PenaltyType? Type { get; set; }
    public decimal? Amount { get; set; }
    public bool? ApplyPerOccurrence { get; set; }
    public int? MaxOccurrencesPerWeek { get; set; } // puede ser null para “sin límite”
    public bool? IsActive { get; set; }
}

public class PayrollPenaltyRuleDto
{
    public int Id { get; set; }
    public int PayrollConfigId { get; set; }
    public PenaltyType Type { get; set; }
    public decimal Amount { get; set; }
    public bool ApplyPerOccurrence { get; set; }
    public int? MaxOccurrencesPerWeek { get; set; }
    public bool IsActive { get; set; }
}
