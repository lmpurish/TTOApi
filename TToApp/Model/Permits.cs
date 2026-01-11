namespace TToApp.Model
{
    public class Permits
    {
        public int Id { get; set; }
        public int WarehouseId { get; set; }
        public Warehouse? Warehouse { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }

        public Permit UserPermit {  get; set; }
    }

    public enum Permit
    {
        Notification,
        Payroll,
      
    }

}
