﻿namespace TicketingSystem.Models
{
    public class TicketsViewModel
    {
        public string TicketId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Priority { get; set; }
        public string Status { get; set; }
        public DateTime SubmittedAt { get; set; }
        public string SubmittedByEmail { get; set; }
        public List<string> imageUrls { get; set; }
    }
}