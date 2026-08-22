namespace OrderingSystem.Infrastructure.Persistence.Seed;

/// <summary>
/// The seeded menus, as plain data. Kept apart from the seeder itself so that changing a price or
/// adding a dish does not mean reading through insert logic.
/// </summary>
internal static class MenuCatalog
{
    internal sealed record OptionSeed(string Name, decimal PriceDelta, int MaxQuantity = 1);

    internal sealed record GroupSeed(
        string Name, int MinSelect, int? MaxSelect, IReadOnlyList<OptionSeed> Options);

    internal sealed record ItemSeed(
        string Name,
        decimal Price,
        string Description,
        IReadOnlyList<string> Groups,
        int? MaxSelectOverrideOnLastGroup = null);

    internal sealed record CategorySeed(string Name, IReadOnlyList<ItemSeed> Items);

    internal sealed record RestaurantSeed(
        string Name,
        string Slug,
        string Description,
        string Phone,
        decimal CommissionPercent,
        decimal MinOrderUsd,
        int PrepMinutes,
        IReadOnlyList<GroupSeed> Groups,
        IReadOnlyList<CategorySeed> Categories,
        IReadOnlyList<(string Zone, decimal Fee, int Minutes)> Delivers);

    /// <summary>Beirut and its near suburbs. Zone is what drives coverage and fees, not distance.</summary>
    public static readonly string[] Zones =
    [
        "Achrafieh", "Hamra", "Verdun", "Gemmayzeh", "Mar Mikhael",
        "Badaro", "Hazmieh", "Furn el Chebbak", "Jounieh", "Dbayeh",
    ];

    public static readonly RestaurantSeed[] Restaurants =
    [
        new(
            Name: "FriesLab",
            Slug: "frieslab",
            Description: "Smashed burgers, loaded fries and signature sauces.",
            Phone: "+9611234567",
            CommissionPercent: 15m,
            MinOrderUsd: 8m,
            PrepMinutes: 20,
            Groups:
            [
                // Required, pick exactly one — a radio group.
                new("Size", 1, 1,
                [
                    new("Regular", 0m),
                    new("Large", 2.00m),
                ]),

                // Optional, unlimited — checkboxes. Extra Patty may be taken twice.
                new("Extras", 0, null,
                [
                    new("Extra Cheese", 1.00m),
                    new("Turkey Bacon", 1.50m),
                    new("Jalapenos", 0.50m),
                    new("Extra Patty", 3.00m, MaxQuantity: 2),
                ]),

                // Optional but capped — "choose up to 3".
                new("Sauces", 0, 3,
                [
                    new("Garlic", 0m),
                    new("Spicy Mayo", 0m),
                    new("BBQ", 0m),
                    new("Ketchup", 0m),
                    new("Lab Ranch", 0.50m),
                ]),

                // Zero-cost removals — proves a delta of exactly 0 is a real case.
                new("Remove", 0, null,
                [
                    new("No Pickles", 0m),
                    new("No Onions", 0m),
                    new("No Sauce", 0m),
                ]),
            ],
            Categories:
            [
                new("Smashed Burgers",
                [
                    new("Classic Smash", 7.50m, "Two smashed patties, cheese, house sauce.",
                        ["Size", "Extras", "Sauces", "Remove"]),
                    new("Double Smash", 10.50m, "Four patties for a serious appetite.",
                        ["Size", "Extras", "Sauces", "Remove"]),
                    new("Bacon Lab", 9.75m, "Turkey bacon, cheddar, crispy onions.",
                        ["Size", "Extras", "Sauces", "Remove"]),
                    new("Spicy Inferno", 9.00m, "Jalapenos, pepper jack, chilli mayo.",
                        ["Size", "Extras", "Sauces", "Remove"]),
                ]),
                new("Loaded Fries",
                [
                    new("Cheese Lab Fries", 6.00m, "Fries under a blanket of cheese sauce.",
                        ["Extras", "Sauces"]),
                    new("Truffle Parmesan Fries", 8.50m, "Truffle oil, parmesan, parsley.",
                        ["Extras", "Sauces"]),
                    new("Chili Cheese Fries", 7.25m, "Beef chilli, cheese sauce, jalapenos.",
                        ["Extras", "Sauces"]),
                ]),
                new("Wings",
                [
                    new("Buffalo Wings", 8.00m, "Six wings in buffalo sauce.", ["Sauces"]),
                    new("BBQ Wings", 8.00m, "Six wings, smoky BBQ glaze.", ["Sauces"]),

                    // The whole reason MaxSelectOverride exists: a shared group, widened for
                    // one item. Twelve wings deserve five sauces, not three.
                    new("Wings Platter", 14.50m, "Twelve wings, mix and match.",
                        ["Sauces"], MaxSelectOverrideOnLastGroup: 5),
                ]),
                new("Drinks",
                [
                    new("Coca-Cola", 1.50m, "330ml can.", []),
                    new("Fresh Lemonade", 3.00m, "Lemon, mint, not too sweet.", []),
                    new("Still Water", 1.00m, "500ml.", []),
                ]),
            ],
            Delivers:
            [
                ("Mar Mikhael", 1.50m, 15), ("Gemmayzeh", 1.50m, 15), ("Achrafieh", 2.00m, 20),
                ("Badaro", 2.50m, 25), ("Furn el Chebbak", 3.00m, 30),
            ]),

        new(
            Name: "Beirut Mezze House",
            Slug: "beirut-mezze-house",
            Description: "Cold and hot mezze, charcoal grills, the way it should be.",
            Phone: "+9611234568",
            CommissionPercent: 12m,
            MinOrderUsd: 15m,
            PrepMinutes: 30,
            Groups:
            [
                new("Bread", 1, 1, [new("Pita", 0m), new("Saj", 1.00m), new("No Bread", 0m)]),
                new("Add-ons", 0, null, [new("Extra Pickles", 0.50m), new("Garlic Dip", 1.00m)]),
            ],
            Categories:
            [
                new("Cold Mezze",
                [
                    new("Hummus", 5.00m, "Chickpea, tahini, olive oil.", ["Bread"]),
                    new("Tabbouleh", 6.00m, "Parsley, tomato, burghul, lemon.", ["Bread"]),
                    new("Moutabal", 5.50m, "Smoked aubergine, tahini.", ["Bread"]),
                ]),
                new("Hot Mezze",
                [
                    new("Kibbeh", 8.00m, "Four pieces, fried.", ["Bread", "Add-ons"]),
                    new("Falafel", 5.00m, "Six pieces, tahini on the side.", ["Bread", "Add-ons"]),
                ]),
                new("Grills",
                [
                    new("Shish Taouk", 12.00m, "Charcoal chicken skewers, garlic.", ["Bread", "Add-ons"]),
                    new("Mixed Grill", 18.00m, "Taouk, kafta and lamb.", ["Bread", "Add-ons"]),
                ]),
            ],
            Delivers:
            [
                ("Achrafieh", 1.50m, 15), ("Badaro", 2.00m, 20), ("Hamra", 3.00m, 30),
                ("Hazmieh", 3.00m, 30), ("Verdun", 3.50m, 35),
            ]),

        new(
            Name: "Shawarma Station",
            Slug: "shawarma-station",
            Description: "Wraps and plates, carved to order.",
            Phone: "+9611234569",
            CommissionPercent: 18m,
            MinOrderUsd: 6m,
            PrepMinutes: 15,
            Groups:
            [
                new("Spice", 1, 1, [new("Mild", 0m), new("Medium", 0m), new("Hot", 0m)]),
                new("Add-ons", 0, null,
                [
                    new("Extra Garlic", 0.50m),
                    new("Fries In The Wrap", 1.00m),
                    new("Extra Pickles", 0m),
                ]),
            ],
            Categories:
            [
                new("Wraps",
                [
                    new("Chicken Shawarma Wrap", 4.50m, "Garlic, pickles, thin bread.", ["Spice", "Add-ons"]),
                    new("Beef Shawarma Wrap", 5.50m, "Tahini, tomato, onion.", ["Spice", "Add-ons"]),
                ]),
                new("Plates",
                [
                    new("Chicken Plate", 11.00m, "Rice, salad, garlic.", ["Spice", "Add-ons"]),
                    new("Mixed Plate", 13.00m, "Chicken and beef.", ["Spice", "Add-ons"]),
                ]),
            ],
            Delivers:
            [
                ("Hamra", 1.00m, 10), ("Verdun", 1.50m, 15), ("Achrafieh", 2.50m, 25),
                ("Gemmayzeh", 2.50m, 25),
            ]),
    ];
}
