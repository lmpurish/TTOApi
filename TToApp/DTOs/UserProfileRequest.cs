using Microsoft.AspNetCore.Http;
using System;
using System.ComponentModel.DataAnnotations;

namespace TToApp.DTOs
{
    public class UserProfileRequest
    {
      
        public string? PhoneNumber { get; set; }
        public string? SocialSecurityNumber { get; set; } // llega completo, se cifra
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? DriverLicenseNumber { get; set; }
        public string? AccountNumber { get; set; }
        public string? RoutingNumber { get; set; }
        public string? AccountHolderName { get; set; }
        public DateTime? ExpInsurance { get; set; }
        public DateTime? DateOfBirth { get; set; }        // llega como DateTime del front
        public DateTime? ExpDriverLicense { get; set; }

        public IFormFile? DriverLicense { get; set; }
        public IFormFile? SocialSecurityUrl { get; set; }
        public IFormFile? AvatarUrl { get; set; }
        public IFormFile? InsuranceUrl { get; set; }

    }

}