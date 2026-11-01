using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Utilities;
using Microsoft.UI.Xaml;

namespace ComponentCatalogWinUI.Models;

public sealed class ComponentItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string PartNumber { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string MajorCategoryId { get; set; } = string.Empty;

    public string SubCategoryId { get; set; } = string.Empty;

    public string Package { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public string Attributes { get; set; } = string.Empty;

    public IReadOnlyList<string> Aliases { get; set; } = [];

    public IReadOnlyList<string> Keywords { get; set; } = [];

    public IReadOnlyList<string> Substitutes { get; set; } = [];

    public string Notes { get; set; } = string.Empty;

    public bool Owned { get; set; }

    public ComponentSourceType SourceType { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public string OwnedBadgeText => Owned ? "持有" : "未持有";

    public Visibility LocationVisibility => string.IsNullOrWhiteSpace(Location)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string CategoryGlyph => MajorCategoryId switch
    {
        "passives" => "\uEADF",
        "mcu-logic" => "\uE899",
        "diodes-transistors" => "\uE725",
        "protection-power" => "\uE945",
        "interface-timing" => "\uE7B3",
        "connectors-switches" => "\uE8CD",
        "data-rf" => "\uE895",
        "opto-display" => "\uE790",
        "filters-oscillators" => "\uE95B",
        "memory-sensors-isolation" => "\uE91B",
        "relays-audio-motors" => "\uEB3D",
        "modules-communication" => "\uE909",
        "amplifiers-development" => "\uE713",
        "other" => "\uE74C",
        _ => "\uE8B7",
    };

    public static ComponentItem FromRecord(ComponentRecord record)
    {
        return new ComponentItem
        {
            Id = record.Id,
            Name = record.Name,
            PartNumber = record.PartNumber,
            Category = record.Category,
            MajorCategoryId = record.MajorCategoryId,
            SubCategoryId = record.SubCategoryId,
            Package = record.Package,
            Location = record.Location,
            Attributes = FormatAttributes(record.Attributes),
            Aliases = record.Aliases.ToList(),
            Keywords = record.Keywords.ToList(),
            Substitutes = record.Substitutes.ToList(),
            Notes = record.Notes,
            Owned = record.Owned,
            SourceType = record.SourceType,
        };
    }

    private static string FormatAttributes(IReadOnlyDictionary<string, string> attributes)
    {
        return string.Join("; ", attributes.Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key}={pair.Value}")));
    }

    public static string FormatList(IReadOnlyList<string> values)
    {
        return CatalogTextCodec.FormatList(values);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
