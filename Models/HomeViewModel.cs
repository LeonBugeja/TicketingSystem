namespace TicketingSystem.Models
{
    public class HomeViewModel
    {
        public List<TicketsViewModel> Tickets { get; set; }
        public bool IsTechnician { get; set; }
    }
}