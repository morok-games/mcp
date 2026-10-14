using System.ComponentModel;
using Platform.Api.Catalog.Entities;

namespace Platform.Api.Assistant;

public enum QueryIntent
{
    /// <summary>Looking for a suitable offering.</summary>
    FindOffering,
    /// <summary>Asking about a specific offering: price, schedule, coach, what is included.</summary>
    OfferingDetails,
    /// <summary>Wants to join, buy or pay.</summary>
    Enroll,
    Compare,
    /// <summary>Question not about the catalog.</summary>
    General,
    SmallTalk
}

/// <summary>
/// Search parameters derived from the user's last message (with the conversation as context).
/// Brand and offering mentions are raw text: matching them to catalog records is done by our code, not by the LLM.
/// </summary>
public sealed class QueryUnderstanding
{
    [Description("""
        FindOffering: looking for a suitable program/product.
        OfferingDetails: asks about a specific offering (price, schedule, coach, what is included, who it is for).
        Enroll: wants to join, sign up, buy or pay.
        Compare: compares several offerings.
        General: question unrelated to the catalog.
        SmallTalk: greetings, thanks.
        """)]
    public required QueryIntent Intent { get; init; }

    [Description("Business/brand names mentioned by the user or referenced from earlier messages, as written, e.g. 'VSA', 'Victory Skating'.")]
    public List<string> BrandMentions { get; init; } = [];

    [Description("Proper names of specific offerings mentioned by the user or referred to from earlier messages ('it', 'this program', 'join'), e.g. 'Double Axel Club'. Not generic descriptions like 'training program' or 'VSA program'.")]
    public List<string> OfferingMentions { get; init; } = [];

    [Description("Search keywords in English, lowercase: skills, goals, topics. For each skill also add its common synonyms and abbreviations, e.g. 'double axel' -> 'double axel', '2axel', '2a'; 'figure skating' -> 'figure skating', 'skating'. No brand names, prices or filler words.")]
    public List<string> Keywords { get; init; } = [];

    [Description("Only if the user states or clearly implies it.")]
    public DeliveryMode? DeliveryMode { get; init; }

    [Description("Only if the user states or clearly implies it: Program for multi-week courses/clubs, Class for a single session, Appointment for private lessons.")]
    public OfferingKind? Kind { get; init; }

    [Description("Upper price limit stated by the user, e.g. 'under $350' -> 350.")]
    public decimal? MaxPrice { get; init; }

    [Description("ISO 4217 code of MaxPrice, e.g. USD.")]
    public string? Currency { get; init; }

    [Description("Desired total length as ISO 8601 duration, e.g. 'six months' -> P6M.")]
    public string? Duration { get; init; }

    [Description("Skill level or experience the user mentions about themselves, as written.")]
    public string? Level { get; init; }

    [Description("ISO 639-1 language of the user's last message; the answer will be in this language.")]
    public required string Language { get; init; }
}
