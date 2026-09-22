using System.Collections.Generic;

namespace bms_editer.Models;

public sealed record LaneDefinition(string Id, string Header)
{
    public static IReadOnlyList<LaneDefinition> CreateDefault()
    {
        return new List<LaneDefinition>
        {
            new("16", "16"),
            new("11", "11"),
            new("12", "12"),
            new("13", "13"),
            new("14", "14"),
            new("15", "15"),
            new("18", "18")
        };
    }
}
