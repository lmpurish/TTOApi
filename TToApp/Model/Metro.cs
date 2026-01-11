namespace TToApp.Model
{
    public class Metro
    {
        public int Id { get; set; }
        public string City { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }
    }
}
