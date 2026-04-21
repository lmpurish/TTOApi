namespace TToApp.Model
{
    public class UserImportMatch
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string ImportedName { get; set; } = string.Empty;
        public string ImportedNameNormalized { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
