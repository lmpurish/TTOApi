using Microsoft.AspNetCore.DataProtection;

namespace TToApp.Services
{
    public interface ISensitiveDataProtector
    {
        string Protect(string plain);
        string Unprotect(string protectedValue);
    }

    public class SensitiveDataProtector : ISensitiveDataProtector
    {
        private readonly IDataProtector _protector;
        public SensitiveDataProtector(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector("SSN_v1");
        }
        public string Protect(string plain) => string.IsNullOrWhiteSpace(plain) ? null : _protector.Protect(plain);
        public string Unprotect(string protectedValue)
        {
            if (string.IsNullOrEmpty(protectedValue)) return null;
            try { return _protector.Unprotect(protectedValue); } catch { return null; }
        }
    }
}
