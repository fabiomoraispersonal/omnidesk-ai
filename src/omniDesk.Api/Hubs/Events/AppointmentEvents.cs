namespace omniDesk.Api.Hubs.Events;

/// <summary>Spec 011 T096 — WS message type and action constants for appointment events.</summary>
public static class AppointmentEvents
{
    public const string Type = "appointment.changed";

    public static class Action
    {
        public const string Created       = "created";
        public const string Confirmed     = "confirmed";
        public const string Cancelled     = "cancelled";
        public const string NoShow        = "no_show";
        public const string Rescheduled   = "rescheduled";
        public const string ReminderSent  = "reminder_sent";
        public const string ReminderResent = "reminder_resent";
    }
}
