namespace TToApp.Model
{
    public class Packages
    {
        public int Id { get; set; }
        public int? RoutesId { get; set; }
        public Routes? Routes { get; set; }
        public string Tracking {  get; set; }
        public string? Address {  get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? Distance {  get; set; }
        public string? ScanLat {  get; set; }
        public string? ScanLon { get; set; }
        public string? AddrLat {  get; set; }
        public string? AddrLon { get; set; }
        public DateTime IncidentDate { get; set; }
        public PackageStatus Status { get; set; } = PackageStatus.RD;
        public int DaysElapsed { get; set; } = 0;
        public bool Notified { get; set; } = false;
        public int? RSP {  get; set; }
        public string? Brand { get; set; }
        public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.Open;
        public decimal? Weight { get; set; }

    }

    public enum PackageStatus
    {
        RD,    // Received of Delivery
        CNL,   // Cancelled/Lost
        CL,   // Delivered
        RTN,    // Returned
        HW,        //
        UD,         //
        CO,      //Company Closed no puede estar dia 2
        NH,        //No se puede dejar, no puede estar en los dias 2 
        OD,        //Out for Delivery, no puede estar en los dias 2 
        WA,    //wrong address, no puede estar en dia 2
        ED,    //end of day, no puede estar dia 2
        UG,    //need access code, no puede estar dia 2

    }

    public enum ReviewStatus
    {
        Pending,    // Pending
        Approved,   // Approved
        Rejected,   // Rejected
        Open,     // Opened
    }
}
