namespace DwgMapOverlayPoc.Models;

public enum EquipmentType { Valve, Pump, Sensor, Custom }

public sealed class EquipmentMarker
{
    /// <summary>Stable ID — uses init so it can be restored from localStorage.</summary>
    public string        Id        { get; init; } = Guid.NewGuid().ToString("N");
    public EquipmentType Type      { get; set; }
    public string        Label     { get; set; } = string.Empty;
    public double        Lat       { get; set; }
    public double        Lng       { get; set; }
    public bool          IsSnapped { get; set; }
}
