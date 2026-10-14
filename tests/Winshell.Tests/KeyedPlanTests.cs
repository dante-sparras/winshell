using FluentAssertions;

namespace Winshell.Tests;

/// <summary>A keyed list keeps, moves, inserts, removes, and builds again only the items that need it.</summary>
[TestClass]
public sealed class KeyedPlanTests
{
    [TestMethod]
    public void The_same_items_change_nothing()
    {
        Plan(["a", "b"], ["a", "b"]).Unchanged.Should().BeTrue();
    }

    [TestMethod]
    public void A_new_item_is_inserted_in_its_place()
    {
        var plan = Plan(["a", "c"], ["a", "b", "c"]);

        plan.Removals.Should().BeEmpty();
        plan.Slots.Should().Equal(
            new KeyedPlan.Slot(0, 0, KeyedPlan.Step.Keep),
            new KeyedPlan.Slot(1, null, KeyedPlan.Step.Insert),
            new KeyedPlan.Slot(2, 2, KeyedPlan.Step.Keep));
    }

    [TestMethod]
    public void A_gone_item_is_removed_from_the_highest_index_first()
    {
        var plan = Plan(["a", "b", "c"], ["b"]);

        plan.Removals.Should().Equal(2, 0);
        plan.Slots.Should().Equal(new KeyedPlan.Slot(0, 0, KeyedPlan.Step.Keep));
    }

    [TestMethod]
    public void A_moved_item_is_kept_and_moved()
    {
        var plan = Plan(["a", "b"], ["b", "a"]);

        plan.Removals.Should().BeEmpty();
        plan.Slots[0].Should().Be(new KeyedPlan.Slot(0, 1, KeyedPlan.Step.Keep));
        plan.Unchanged.Should().BeFalse();
    }

    [TestMethod]
    public void An_item_whose_value_changed_is_built_again()
    {
        var plan = KeyedPlan.Build(["a"], [1], ["a"], [2], (x, y) => x == y);

        plan.Slots.Should().Equal(new KeyedPlan.Slot(0, 0, KeyedPlan.Step.Replace));
    }

    [TestMethod]
    public void Two_items_with_one_key_are_told_apart_by_order()
    {
        var plan = Plan(["x", "x"], ["x"]);

        plan.Removals.Should().Equal(1);
        plan.Slots.Should().Equal(new KeyedPlan.Slot(0, 0, KeyedPlan.Step.Keep));
    }

    private static KeyedPlan Plan(string[] shown, string[] desired) =>
        KeyedPlan.Build(shown, shown, desired, desired, string.Equals);
}
