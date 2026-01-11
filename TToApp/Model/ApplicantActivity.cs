namespace TToApp.Model
{
    public class ApplicantActivity
    {
        public int Id { get; set; }
        public int ApplicantId { get; set; }
        public int RecruiterId { get; set; }
        public User? Recruiter { get; set; }
        public ActivityType Activity { get; set; }
        public string Message { get; set; }
        public DateOnly CreateAt { get; set; } = new DateOnly();
        public DateTimeOffset? activityDate { get; set; }


        public enum ActivityType
        {
           Note,
           CallScheduled,
           CallOutcome,
           DocRequest,
           StageChange,
           ContractSent,
           ContractSigned

        }

    }

}
