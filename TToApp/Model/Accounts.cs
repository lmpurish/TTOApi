namespace TToApp.Model
{
    public class Accounts
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string AccountNumber { get; set; }
        public string RoutingNumber { get; set; }
        public int UserId {  get; set; }
        public bool IsDefault { get; set; } = false;
        public User? User { get; set; }

    }
}
