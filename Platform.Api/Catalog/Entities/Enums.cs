namespace Platform.Api.Catalog.Entities;

/// <summary>Platform the record was imported from. Manual = entered directly in our system.</summary>
public enum SourcePlatform
{
    Manual,
    Shopify,
    WordPress,
    Mindbody,
    Fresha,
    /// <summary>Website built on Tilda (no product API; data is read from page HTML).</summary>
    Tilda,
    Custom
}

/// <summary>What is being sold. Determines which Offering fields are relevant.</summary>
public enum OfferingKind
{
    /// <summary>Multi-session program over a period (course, club, bootcamp).</summary>
    Program,
    /// <summary>Single scheduled group session.</summary>
    Class,
    /// <summary>Individually booked service (lesson, haircut, massage).</summary>
    Appointment,
    Membership,
    Product,
    Digital
}

public enum OfferingStatus
{
    Draft,
    Active,
    Archived
}

public enum DeliveryMode
{
    Online,
    InPerson,
    Hybrid
}

public enum BillingType
{
    OneTime,
    Recurring,
    Installments
}

public enum BillingInterval
{
    Week,
    Month,
    Year
}

public enum EnrollmentStatus
{
    ComingSoon,
    Open,
    Waitlist,
    Closed
}
