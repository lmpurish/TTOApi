namespace TToApp.Services.CommunicationRecipient
{
    public interface ICommunicationRecipientService
    {
        Task<List<User>> GetRecipientsForEventAsync(int companyId,
            int? warehouseId,
            string eventType,
            string channel);
    }
}