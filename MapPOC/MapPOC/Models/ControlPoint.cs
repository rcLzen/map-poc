namespace MapPOC.Models;

public sealed class ControlPoint
{
    public int Index { get; set; }
    public double PixelX { get; set; }
    public double PixelY { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool IsSet { get; set; }
}
