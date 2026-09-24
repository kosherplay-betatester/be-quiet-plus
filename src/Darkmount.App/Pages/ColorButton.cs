using System.ComponentModel;

namespace Darkmount.App.Pages;

/// <summary>A swatch button that opens the colour picker and shows the chosen colour.</summary>
public sealed class ColorButton : Button
{
    Color _value = Color.FromArgb(0xFF, 0x28, 0x00);

    public ColorButton()
    {
        FlatStyle = FlatStyle.Flat;
        Size = new Size(46, 30);
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 4, 8, 4);
        Apply();
    }

    public event Action<ColorButton>? ValueChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Value
    {
        get => _value;
        set { _value = value; Apply(); }
    }

    void Apply()
    {
        BackColor = _value;
        FlatAppearance.BorderColor = Color.FromArgb(90, 96, 108);
        FlatAppearance.MouseOverBackColor = _value;
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        using var dlg = new ColorDialog { Color = _value, FullOpen = true, AnyColor = true };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        Value = dlg.Color;
        ValueChanged?.Invoke(this);
    }
}
