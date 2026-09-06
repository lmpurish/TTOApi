namespace TToApp.Services.CommunicationRecipient
{
    public interface ICommunicationRecipientService
    {
        Task<List<User>> GetRecipientsForEventAsync(int companyId,
            IEnumerable<int>? warehouseIds,
            string eventType,
            string channel,
            bool includePermitUsers = true);
    }
}