using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Http.HttpResults;
namespace TToApp.Model
{
    public class CompanyRevenue
    {
        public long Id { get; set; }

        public int CompanyId { get; set; }
        public Company Company { get; set; } = null!;

        public long PayPeriodId { get; set; }
        public PayPeriod PayPeriod { get; set; } = null!;

        public int? WarehouseId { get; set; }
        public Warehouse? Warehouse { get; set; }

        public decimal Revenue { get; set; }
        public decimal Expenses { get; set; }
        public decimal Adjustments { get; set; }

        public string RevenueType { get; set; } = "Settlement";
        public string? Notes { get; set; }
        public string? AttachmentUrl { get; set; }

        public DateTime RevenueDate { get; set; } = DateTime.UtcNow;

        public int CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }


    public static class CompanyRevenueEndpoints
{
	public static void MapCompanyRevenueEndpoints (this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/CompanyRevenue").WithTags(nameof(CompanyRevenue));

        group.MapGet("/", () =>
        {
            return new [] { new CompanyRevenue() };
        })
        .WithName("GetAllCompanyRevenues")
        .WithOpenApi();

        group.MapGet("/{id}", (int id) =>
        {
            //return new CompanyRevenue { ID = id };
        })
        .WithName("GetCompanyRevenueById")
        .WithOpenApi();

        group.MapPut("/{id}", (int id, CompanyRevenue input) =>
        {
            return TypedResults.NoContent();
        })
        .WithName("UpdateCompanyRevenue")
        .WithOpenApi();

        group.MapPost("/", (CompanyRevenue model) =>
        {
            //return TypedResults.Created($"/api/CompanyRevenues/{model.ID}", model);
        })
        .WithName("CreateCompanyRevenue")
        .WithOpenApi();

        group.MapDelete("/{id}", (int id) =>
        {
            //return TypedResults.Ok(new CompanyRevenue { ID = id });
        })
        .WithName("DeleteCompanyRevenue")
        .WithOpenApi();
    }
}}
