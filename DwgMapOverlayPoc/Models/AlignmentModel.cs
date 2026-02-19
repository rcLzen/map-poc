namespace DwgMapOverlayPoc.Models;

/// <summary>One pixel ↔ world coordinate pair used for 3-point georeferencing.</summary>
public sealed class ControlPoint
{
    public double ImageX    { get; set; }
    public double ImageY    { get; set; }
    public double WorldLat  { get; set; }
    public double WorldLng  { get; set; }

    /// <summary>Set to true after the user clicks the map for this point.</summary>
    public bool HasMapCoords { get; set; }
}

public enum AlignmentStatus { Idle, Picking, Ready, Error }
