namespace OrderingSystem.Domain.Enums;

/// <summary>What a person may do inside one restaurant.</summary>
public enum StaffRoleType
{
    /// <summary>Sees orders, changes order status, edits the menu.</summary>
    Staff = 1,

    /// <summary>Everything Staff can do, plus staff accounts, zones, fees and prep time.</summary>
    Owner = 2,
}
