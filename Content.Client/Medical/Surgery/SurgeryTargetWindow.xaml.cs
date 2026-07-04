using System;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Medical.Surgery;

public sealed partial class SurgeryTargetWindow : FancyWindow
{
    public event Action<string>? OnZoneSelected;

    public SurgeryTargetWindow()
    {
        Title = "Surgery Target";
        MinWidth = 220;

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(4)
        };

        root.AddChild(new Label
        {
            Text = "Select body part:",
            StyleClasses = { "LabelKeyText" },
            Margin = new Thickness(0, 0, 0, 4)
        });

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        row.AddChild(MakeColumn("Head", "Torso", "Groin"));
        row.AddChild(MakeColumn("L-Arm", "L-Hand", "L-Leg", "L-Foot"));
        row.AddChild(MakeColumn("R-Arm", "R-Hand", "R-Leg", "R-Foot"));
        root.AddChild(row);

        ContentsContainer.AddChild(root);
    }

    private BoxContainer MakeColumn(params string[] labels)
    {
        var col = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SizeFlagsStretchRatio = 1,
            Margin = new Thickness(2)
        };

        foreach (var label in labels)
        {
            var zone = MapLabelToZone(label);
            var btn = new Button { Text = label, MinSize = new Vector2(70, 28) };
            btn.OnPressed += _ => OnZoneSelected?.Invoke(zone);
            col.AddChild(btn);
        }

        return col;
    }

    private static string MapLabelToZone(string label)
    {
        return label switch
        {
            "L-Arm" => "ArmLeft",
            "R-Arm" => "ArmRight",
            "L-Hand" => "HandLeft",
            "R-Hand" => "HandRight",
            "L-Leg" => "LegLeft",
            "R-Leg" => "LegRight",
            "L-Foot" => "FootLeft",
            "R-Foot" => "FootRight",
            _ => label // Head, Torso, Groin
        };
    }
}
